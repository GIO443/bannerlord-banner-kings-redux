"""Chunk WIKI.md, embed, persist to ChromaDB.

Run on first deploy and after every wiki edit:
    python ingest.py
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

import chromadb
from chromadb.config import Settings
from dotenv import load_dotenv
from sentence_transformers import SentenceTransformer

load_dotenv()

WIKI_PATH = Path(os.getenv("WIKI_PATH", "../docs/WIKI.md"))
CHROMA_DIR = os.getenv("CHROMA_DIR", "./chroma_db")
CHUNK_TOKENS = int(os.getenv("CHUNK_TOKENS", "500"))
CHUNK_OVERLAP = int(os.getenv("CHUNK_OVERLAP", "50"))
COLLECTION = "bk_wiki"
EMBED_MODEL = "sentence-transformers/all-MiniLM-L6-v2"


def split_by_headings(text: str) -> list[tuple[str, str]]:
    """Split markdown into (heading_path, body) sections.

    Tracks the running heading hierarchy so each chunk knows its location
    (e.g. "4. Major systems > 4.3 Religion").
    """
    lines = text.splitlines()
    sections: list[tuple[str, list[str]]] = []
    stack: list[str] = []
    buf: list[str] = []

    def flush():
        if buf:
            sections.append((" > ".join(stack) if stack else "", buf.copy()))
            buf.clear()

    for line in lines:
        m = re.match(r"^(#{1,6})\s+(.+)$", line)
        if m:
            flush()
            level = len(m.group(1))
            title = m.group(2).strip()
            stack = stack[: level - 1]
            while len(stack) < level - 1:
                stack.append("")
            stack.append(title)
        else:
            buf.append(line)
    flush()
    return [(h, "\n".join(b).strip()) for h, b in sections if "\n".join(b).strip()]


def approx_tokens(s: str) -> int:
    # crude: ~4 chars/token for english
    return max(1, len(s) // 4)


def window_chunk(body: str, max_tokens: int, overlap: int) -> list[str]:
    """Sliding window over paragraphs by approximate token budget."""
    paras = [p.strip() for p in re.split(r"\n\s*\n", body) if p.strip()]
    chunks: list[str] = []
    cur: list[str] = []
    cur_tok = 0
    for p in paras:
        t = approx_tokens(p)
        if cur and cur_tok + t > max_tokens:
            chunks.append("\n\n".join(cur))
            # overlap = last paragraph(s) summing to ~overlap tokens
            tail: list[str] = []
            tail_tok = 0
            for q in reversed(cur):
                tail.insert(0, q)
                tail_tok += approx_tokens(q)
                if tail_tok >= overlap:
                    break
            cur = tail
            cur_tok = tail_tok
        cur.append(p)
        cur_tok += t
    if cur:
        chunks.append("\n\n".join(cur))
    return chunks


def build_chunks(text: str) -> list[dict]:
    out: list[dict] = []
    for heading, body in split_by_headings(text):
        for i, ch in enumerate(window_chunk(body, CHUNK_TOKENS, CHUNK_OVERLAP)):
            out.append(
                {
                    "id": f"{heading}#{i}" if heading else f"_root#{i}",
                    "heading": heading,
                    "text": (f"[{heading}]\n{ch}" if heading else ch),
                }
            )
    return out


def main() -> int:
    if not WIKI_PATH.exists():
        print(f"WIKI not found at {WIKI_PATH.resolve()}", file=sys.stderr)
        return 1

    text = WIKI_PATH.read_text(encoding="utf-8")
    chunks = build_chunks(text)
    print(f"Built {len(chunks)} chunks from {WIKI_PATH}")

    print(f"Loading embedding model: {EMBED_MODEL}")
    model = SentenceTransformer(EMBED_MODEL)
    embeddings = model.encode(
        [c["text"] for c in chunks], show_progress_bar=True, normalize_embeddings=True
    ).tolist()

    client = chromadb.PersistentClient(path=CHROMA_DIR, settings=Settings(anonymized_telemetry=False))
    # Recreate collection so re-ingest is idempotent.
    try:
        client.delete_collection(COLLECTION)
    except Exception:
        pass
    coll = client.create_collection(COLLECTION, metadata={"hnsw:space": "cosine"})
    coll.add(
        ids=[c["id"] for c in chunks],
        documents=[c["text"] for c in chunks],
        embeddings=embeddings,
        metadatas=[{"heading": c["heading"]} for c in chunks],
    )
    print(f"Indexed {len(chunks)} chunks into '{COLLECTION}' at {CHROMA_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
