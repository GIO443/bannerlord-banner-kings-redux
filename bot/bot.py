"""BannerKings Discord RAG bot.

Slash commands:
  /ask <question>            — FAQ check, then RAG fallback
  /faq_add <q> <a>           — (admin) manually add FAQ entry
  /faq_list                  — list current FAQ entries

Pipeline per /ask:
  1. embed query
  2. cosine vs FAQ table (>= FAQ_HIT_THRESHOLD => return cached answer)
  3. else top-K chunks from Chroma -> Groq (fallback Gemini) -> reply
  4. log query + answer to SQLite for nightly clustering
"""
from __future__ import annotations

import asyncio
import logging
import os
import sqlite3
import time
from dataclasses import dataclass
from pathlib import Path

import chromadb
import discord
import numpy as np
from chromadb.config import Settings
from discord import app_commands
from dotenv import load_dotenv
from sentence_transformers import SentenceTransformer

load_dotenv()
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
log = logging.getLogger("bk-bot")

DISCORD_TOKEN = os.environ["DISCORD_TOKEN"]
GUILD_ID = int(os.environ["DISCORD_GUILD_ID"]) if os.getenv("DISCORD_GUILD_ID") else None
GROQ_KEY = os.getenv("GROQ_API_KEY")
GROQ_MODEL = os.getenv("GROQ_MODEL", "llama-3.3-70b-versatile")
GEMINI_KEY = os.getenv("GEMINI_API_KEY")
GEMINI_MODEL = os.getenv("GEMINI_MODEL", "gemini-2.0-flash")
CHROMA_DIR = os.getenv("CHROMA_DIR", "./chroma_db")
SQLITE_PATH = os.getenv("SQLITE_PATH", "./bk_bot.sqlite")
FAQ_THRESHOLD = float(os.getenv("FAQ_HIT_THRESHOLD", "0.85"))
TOP_K = int(os.getenv("RAG_TOP_K", "5"))

EMBED_MODEL = "sentence-transformers/all-MiniLM-L6-v2"
EMBED_DIM = 384

SYSTEM_PROMPT = (
    "You are the BannerKings mod assistant. Answer ONLY using the wiki context "
    "below. If the context does not contain the answer, say so plainly — do not "
    "invent details. Be concise (max ~150 words). Cite the section heading you "
    "drew from in the form [Section: <heading>]."
)


# ---------- storage ----------

def db_init() -> sqlite3.Connection:
    conn = sqlite3.connect(SQLITE_PATH)
    conn.execute(
        """CREATE TABLE IF NOT EXISTS faq (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            question TEXT NOT NULL,
            answer TEXT NOT NULL,
            embedding BLOB NOT NULL,
            hits INTEGER DEFAULT 0,
            created_at INTEGER NOT NULL,
            last_hit_at INTEGER
        )"""
    )
    # Forward-compat: add last_hit_at if upgrading from older schema.
    cols = {row[1] for row in conn.execute("PRAGMA table_info(faq)").fetchall()}
    if "last_hit_at" not in cols:
        conn.execute("ALTER TABLE faq ADD COLUMN last_hit_at INTEGER")
    conn.execute(
        """CREATE TABLE IF NOT EXISTS query_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            user_id TEXT, question TEXT, answer TEXT,
            faq_hit INTEGER, embedding BLOB,
            created_at INTEGER NOT NULL
        )"""
    )
    conn.commit()
    return conn


def emb_to_blob(v: np.ndarray) -> bytes:
    return v.astype(np.float32).tobytes()


def blob_to_emb(b: bytes) -> np.ndarray:
    return np.frombuffer(b, dtype=np.float32)


# ---------- retrieval ----------

@dataclass
class FAQHit:
    question: str
    answer: str
    score: float
    faq_id: int


class Brain:
    def __init__(self) -> None:
        log.info("loading embed model %s", EMBED_MODEL)
        self.embedder = SentenceTransformer(EMBED_MODEL)
        self.chroma = chromadb.PersistentClient(
            path=CHROMA_DIR, settings=Settings(anonymized_telemetry=False)
        )
        try:
            self.coll = self.chroma.get_collection("bk_wiki")
        except Exception as e:
            raise RuntimeError(
                "Chroma collection 'bk_wiki' missing. Run `python ingest.py` first."
            ) from e
        self.db = db_init()

    def embed(self, text: str) -> np.ndarray:
        return self.embedder.encode([text], normalize_embeddings=True)[0]

    def faq_lookup(self, q_emb: np.ndarray) -> FAQHit | None:
        rows = self.db.execute(
            "SELECT id, question, answer, embedding FROM faq"
        ).fetchall()
        if not rows:
            return None
        best: FAQHit | None = None
        for fid, q, a, blob in rows:
            v = blob_to_emb(blob)
            score = float(np.dot(q_emb, v))  # both unit vectors -> cosine
            if best is None or score > best.score:
                best = FAQHit(question=q, answer=a, score=score, faq_id=fid)
        if best and best.score >= FAQ_THRESHOLD:
            return best
        return None

    def rag_context(self, q: str) -> list[str]:
        res = self.coll.query(query_texts=[q], n_results=TOP_K)
        return res.get("documents", [[]])[0]

    def log_query(
        self,
        user_id: str,
        q: str,
        ans: str,
        emb: np.ndarray,
        faq_hit: bool,
    ) -> None:
        self.db.execute(
            "INSERT INTO query_log (user_id, question, answer, faq_hit, embedding, created_at) "
            "VALUES (?, ?, ?, ?, ?, ?)",
            (user_id, q, ans, int(faq_hit), emb_to_blob(emb), int(time.time())),
        )
        self.db.commit()

    def bump_faq_hit(self, faq_id: int) -> None:
        self.db.execute(
            "UPDATE faq SET hits = hits + 1, last_hit_at = ? WHERE id = ?",
            (int(time.time()), faq_id),
        )
        self.db.commit()


# ---------- LLM clients ----------

async def call_groq(question: str, context: list[str]) -> str | None:
    if not GROQ_KEY:
        return None
    try:
        from groq import AsyncGroq

        client = AsyncGroq(api_key=GROQ_KEY)
        ctx = "\n\n---\n\n".join(context)
        resp = await client.chat.completions.create(
            model=GROQ_MODEL,
            messages=[
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": f"Wiki context:\n{ctx}\n\nQuestion: {question}"},
            ],
            temperature=0.2,
            max_tokens=400,
        )
        return resp.choices[0].message.content.strip()
    except Exception as e:
        log.warning("groq failed: %s", e)
        return None


async def call_gemini(question: str, context: list[str]) -> str | None:
    if not GEMINI_KEY:
        return None
    try:
        import google.generativeai as genai

        genai.configure(api_key=GEMINI_KEY)
        model = genai.GenerativeModel(GEMINI_MODEL, system_instruction=SYSTEM_PROMPT)
        ctx = "\n\n---\n\n".join(context)
        loop = asyncio.get_running_loop()
        resp = await loop.run_in_executor(
            None,
            lambda: model.generate_content(
                f"Wiki context:\n{ctx}\n\nQuestion: {question}",
                generation_config={"temperature": 0.2, "max_output_tokens": 400},
            ),
        )
        return resp.text.strip()
    except Exception as e:
        log.warning("gemini failed: %s", e)
        return None


async def answer_via_llm(question: str, context: list[str]) -> str:
    for fn in (call_groq, call_gemini):
        out = await fn(question, context)
        if out:
            return out
    return "Both LLM providers failed. Try again in a minute."


# ---------- discord ----------

intents = discord.Intents.default()
client = discord.Client(intents=intents)
tree = app_commands.CommandTree(client)
brain: Brain  # initialised in main()


def is_admin(interaction: discord.Interaction) -> bool:
    perms = interaction.user.guild_permissions if interaction.guild else None
    return bool(perms and (perms.administrator or perms.manage_guild))


@tree.command(name="ask", description="Ask a question about the BannerKings mod")
@app_commands.describe(question="Your question")
async def ask(interaction: discord.Interaction, question: str) -> None:
    await interaction.response.defer(thinking=True)
    q_emb = brain.embed(question)

    hit = brain.faq_lookup(q_emb)
    if hit:
        brain.bump_faq_hit(hit.faq_id)
        brain.log_query(str(interaction.user.id), question, hit.answer, q_emb, True)
        await interaction.followup.send(
            embed=discord.Embed(
                title="From the FAQ",
                description=hit.answer,
                colour=discord.Colour.green(),
            ).set_footer(text=f"matched: {hit.question} (score {hit.score:.2f})")
        )
        return

    context = brain.rag_context(question)
    if not context:
        ans = "No wiki context retrieved — the index may be empty."
    else:
        ans = await answer_via_llm(question, context)

    brain.log_query(str(interaction.user.id), question, ans, q_emb, False)
    await interaction.followup.send(
        embed=discord.Embed(
            title="Wiki answer",
            description=ans[:4000],
            colour=discord.Colour.blue(),
        ).set_footer(text=f"sources: {len(context)} chunks")
    )


@tree.command(name="faq_add", description="(admin) Add an FAQ entry")
async def faq_add(interaction: discord.Interaction, question: str, answer: str) -> None:
    if not is_admin(interaction):
        await interaction.response.send_message("Admin only.", ephemeral=True)
        return
    emb = brain.embed(question)
    brain.db.execute(
        "INSERT INTO faq (question, answer, embedding, created_at) VALUES (?, ?, ?, ?)",
        (question, answer, emb_to_blob(emb), int(time.time())),
    )
    brain.db.commit()
    await interaction.response.send_message(f"Added FAQ entry: {question}", ephemeral=True)


@tree.command(name="faq_remove", description="(admin) Remove an FAQ entry by id")
async def faq_remove(interaction: discord.Interaction, faq_id: int) -> None:
    if not is_admin(interaction):
        await interaction.response.send_message("Admin only.", ephemeral=True)
        return
    cur = brain.db.execute("DELETE FROM faq WHERE id = ?", (faq_id,))
    brain.db.commit()
    msg = f"Removed entry {faq_id}." if cur.rowcount else f"No entry with id {faq_id}."
    await interaction.response.send_message(msg, ephemeral=True)


@tree.command(name="faq_prune", description="(admin) Show FAQ entries unused for N days")
async def faq_prune(interaction: discord.Interaction, days: int = 90) -> None:
    if not is_admin(interaction):
        await interaction.response.send_message("Admin only.", ephemeral=True)
        return
    cutoff = int(time.time()) - days * 86400
    rows = brain.db.execute(
        "SELECT id, question, hits FROM faq "
        "WHERE (last_hit_at IS NULL AND created_at < ?) "
        "   OR (last_hit_at IS NOT NULL AND last_hit_at < ?) "
        "ORDER BY hits ASC, id ASC LIMIT 25",
        (cutoff, cutoff),
    ).fetchall()
    if not rows:
        await interaction.response.send_message(
            f"Nothing unused for >{days} days.", ephemeral=True
        )
        return
    body = "\n".join(f"`{i}` ({h} hits) {q}" for i, q, h in rows)
    await interaction.response.send_message(
        f"Prune candidates (unused >{days} days). Use `/faq_remove <id>`.\n{body[:1800]}",
        ephemeral=True,
    )


@tree.command(name="faq_list", description="List current FAQ entries")
async def faq_list(interaction: discord.Interaction) -> None:
    rows = brain.db.execute(
        "SELECT id, question, hits FROM faq ORDER BY hits DESC, id DESC LIMIT 25"
    ).fetchall()
    if not rows:
        await interaction.response.send_message("No FAQ entries yet.", ephemeral=True)
        return
    body = "\n".join(f"`{i}` ({h} hits) {q}" for i, q, h in rows)
    await interaction.response.send_message(body[:1900], ephemeral=True)


@client.event
async def on_ready() -> None:
    if GUILD_ID:
        guild = discord.Object(id=GUILD_ID)
        tree.copy_global_to(guild=guild)
        await tree.sync(guild=guild)
        log.info("synced commands to guild %s", GUILD_ID)
    else:
        await tree.sync()
        log.info("synced commands globally")
    log.info("logged in as %s", client.user)


def main() -> None:
    global brain
    brain = Brain()
    client.run(DISCORD_TOKEN)


if __name__ == "__main__":
    main()
