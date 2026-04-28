"""Nightly job: cluster logged misses, post FAQ candidates to mod-review channel.

Run via cron:
    0 4 * * *  /opt/bk-bot/.venv/bin/python /opt/bk-bot/cluster.py

Reads query_log rows where faq_hit=0 and created_at within window. Runs DBSCAN
on embeddings. For each cluster of size >= CLUSTER_MIN_SIZE, picks the medoid
question, asks the LLM to draft a canonical answer using the wiki context,
and posts a "react with thumbs up to approve" message to the FAQ review channel.

A separate listener in bot.py could watch for the reaction and INSERT into faq;
in this minimal version we post the candidate as text and admins approve via
/faq_add. Keeps the bot's hot path simple.
"""
from __future__ import annotations

import asyncio
import logging
import os
import sqlite3
import time

import numpy as np
from dotenv import load_dotenv
from sklearn.cluster import DBSCAN

from bot import (  # type: ignore
    EMBED_MODEL,
    answer_via_llm,
    blob_to_emb,
)

load_dotenv()
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("bk-cluster")

SQLITE_PATH = os.getenv("SQLITE_PATH", "./bk_bot.sqlite")
WINDOW_DAYS = int(os.getenv("CLUSTER_WINDOW_DAYS", "14"))
MIN_SIZE = int(os.getenv("CLUSTER_MIN_SIZE", "3"))
EPS = float(os.getenv("CLUSTER_EPS", "0.25"))  # cosine distance
PRUNE_DAYS = int(os.getenv("FAQ_PRUNE_DAYS", "90"))
LOG_RETENTION_DAYS = int(os.getenv("LOG_RETENTION_DAYS", "180"))
REVIEW_CHANNEL = os.getenv("DISCORD_FAQ_REVIEW_CHANNEL_ID")


def fetch_misses(conn: sqlite3.Connection) -> list[tuple[int, str, np.ndarray]]:
    cutoff = int(time.time()) - WINDOW_DAYS * 86400
    rows = conn.execute(
        "SELECT id, question, embedding FROM query_log "
        "WHERE faq_hit = 0 AND created_at >= ?",
        (cutoff,),
    ).fetchall()
    return [(rid, q, blob_to_emb(b)) for rid, q, b in rows]


def cluster(items: list[tuple[int, str, np.ndarray]]) -> dict[int, list[int]]:
    if len(items) < MIN_SIZE:
        return {}
    X = np.vstack([v for _, _, v in items])
    # cosine distance between unit vectors = 1 - dot
    d = DBSCAN(eps=EPS, min_samples=MIN_SIZE, metric="cosine").fit(X)
    out: dict[int, list[int]] = {}
    for idx, label in enumerate(d.labels_):
        if label == -1:
            continue
        out.setdefault(int(label), []).append(idx)
    return out


def medoid(indices: list[int], items: list[tuple[int, str, np.ndarray]]) -> int:
    """Index (within `indices`) of the most central question in the cluster."""
    vecs = np.vstack([items[i][2] for i in indices])
    sims = vecs @ vecs.T
    avg = sims.mean(axis=1)
    return indices[int(np.argmax(avg))]


async def draft_answer(question: str) -> str:
    # Reuse bot.py's RAG path for context retrieval.
    from bot import Brain  # local import to keep cron startup light

    brain = Brain()
    ctx = brain.rag_context(question)
    return await answer_via_llm(question, ctx)


def find_prune_candidates(conn: sqlite3.Connection) -> list[tuple[int, str, int]]:
    cutoff = int(time.time()) - PRUNE_DAYS * 86400
    return conn.execute(
        "SELECT id, question, hits FROM faq "
        "WHERE (last_hit_at IS NULL AND created_at < ?) "
        "   OR (last_hit_at IS NOT NULL AND last_hit_at < ?)",
        (cutoff, cutoff),
    ).fetchall()


def trim_query_log(conn: sqlite3.Connection) -> int:
    cutoff = int(time.time()) - LOG_RETENTION_DAYS * 86400
    cur = conn.execute("DELETE FROM query_log WHERE created_at < ?", (cutoff,))
    conn.commit()
    return cur.rowcount


async def post_to_channel(
    candidates: list[tuple[str, str, int]],
    prune: list[tuple[int, str, int]],
) -> None:
    if not REVIEW_CHANNEL or (not candidates and not prune):
        for q, a, n in candidates:
            print(f"[ADD cluster={n}] {q}\n  -> {a}\n")
        for fid, q, h in prune:
            print(f"[PRUNE id={fid} hits={h}] {q}")
        return

    import discord

    token = os.environ["DISCORD_TOKEN"]
    intents = discord.Intents.default()
    client = discord.Client(intents=intents)

    @client.event
    async def on_ready() -> None:
        ch = client.get_channel(int(REVIEW_CHANNEL))
        if ch is None:
            ch = await client.fetch_channel(int(REVIEW_CHANNEL))
        for q, a, n in candidates:
            embed = discord.Embed(
                title=f"FAQ candidate (cluster size {n})",
                description=f"**Q:** {q}\n\n**A:** {a}",
                colour=discord.Colour.gold(),
            )
            embed.set_footer(text="If accurate, run /faq_add with this Q and A.")
            await ch.send(embed=embed)
        if prune:
            body = "\n".join(f"`{fid}` ({h} hits) {q}" for fid, q, h in prune[:25])
            embed = discord.Embed(
                title=f"FAQ prune candidates (unused >{PRUNE_DAYS} days)",
                description=body[:3800],
                colour=discord.Colour.red(),
            )
            embed.set_footer(text="Remove with /faq_remove <id>.")
            await ch.send(embed=embed)
        await client.close()

    await client.start(token)


async def main() -> None:
    conn = sqlite3.connect(SQLITE_PATH)
    items = fetch_misses(conn)
    log.info("loaded %d miss rows in %d-day window", len(items), WINDOW_DAYS)
    clusters = cluster(items)
    log.info("found %d clusters of size >= %d", len(clusters), MIN_SIZE)

    candidates: list[tuple[str, str, int]] = []
    for label, idxs in clusters.items():
        m_idx = medoid(idxs, items)
        q = items[m_idx][1]
        ans = await draft_answer(q)
        candidates.append((q, ans, len(idxs)))
        log.info("cluster %s: %s (n=%d)", label, q[:80], len(idxs))

    prune = find_prune_candidates(conn)
    log.info("found %d prune candidates (unused >%d days)", len(prune), PRUNE_DAYS)

    deleted = trim_query_log(conn)
    log.info("trimmed %d old query_log rows (>%d days)", deleted, LOG_RETENTION_DAYS)

    await post_to_channel(candidates, prune)


if __name__ == "__main__":
    asyncio.run(main())
