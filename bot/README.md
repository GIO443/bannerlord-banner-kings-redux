# BannerKings Discord RAG bot

Free-tier Discord bot that answers BannerKings questions from `docs/WIKI.md`.
FAQ cache first; on miss, retrieval-augmented generation against the wiki.

## Stack

| Concern | Choice | Cost |
|---|---|---|
| Host | Oracle Cloud Free Tier ARM (Ampere A1, 4 OCPU / 24 GB) | $0 |
| Embeddings | `all-MiniLM-L6-v2` local (CPU) | $0 |
| Vector store | ChromaDB (SQLite-backed) | $0 |
| FAQ + logs | SQLite | $0 |
| LLM (primary) | Groq, Llama 3.3 70B | free tier, ~30 req/min |
| LLM (fallback) | Google Gemini 2.0 Flash | free tier, 1500 req/day |
| Bot lib | `discord.py` 2.4 | $0 |

## Files

```
bot/
├── requirements.txt
├── .env.example          copy to .env, fill in
├── ingest.py             chunk WIKI.md -> Chroma
├── bot.py                Discord bot, /ask /faq_add /faq_list
├── cluster.py            nightly: cluster misses, post FAQ candidates
└── README.md
```

## Local quickstart

```bash
cd bot
python -m venv .venv
. .venv/bin/activate          # Windows: .venv\Scripts\activate
pip install -r requirements.txt
cp .env.example .env          # fill DISCORD_TOKEN, GROQ_API_KEY, etc.
python ingest.py              # builds chroma_db/
python bot.py                 # runs the bot
```

Re-run `python ingest.py` after every wiki edit.

## API keys (all free)

- **Discord bot token**: https://discord.com/developers/applications → New Application → Bot → Reset Token. Enable the `applications.commands` scope when generating an invite URL.
- **Groq**: https://console.groq.com/keys (no card required).
- **Gemini**: https://aistudio.google.com/apikey (no card required).

## Oracle Cloud Free Tier deploy (ARM Ampere)

### 1. Provision the VM

In Oracle Cloud → Compute → Instances → Create:

- Shape: **VM.Standard.A1.Flex**, 2 OCPU / 12 GB RAM (well under the always-free limit).
- Image: **Canonical Ubuntu 22.04** (ARM64).
- Networking: default VCN; download the SSH private key.

SSH in:

```bash
ssh -i ~/oci_key ubuntu@<public-ip>
```

### 2. System deps

```bash
sudo apt update && sudo apt install -y python3.11-venv python3-pip git build-essential
git clone https://github.com/<you>/bannerlord-banner-kings.git
cd bannerlord-banner-kings/bot
python3.11 -m venv .venv
. .venv/bin/activate
pip install --upgrade pip
pip install -r requirements.txt
cp .env.example .env
nano .env   # paste tokens
```

ARM wheel note: `sentence-transformers` and `chromadb` both ship aarch64 wheels;
nothing to compile from source.

### 3. First ingest

```bash
python ingest.py
```

Expect ~30 s on first run (model download is ~90 MB). Subsequent runs are seconds.

### 4. systemd service

```bash
sudo tee /etc/systemd/system/bk-bot.service > /dev/null <<EOF
[Unit]
Description=BannerKings Discord RAG bot
After=network-online.target

[Service]
Type=simple
User=ubuntu
WorkingDirectory=/home/ubuntu/bannerlord-banner-kings/bot
EnvironmentFile=/home/ubuntu/bannerlord-banner-kings/bot/.env
ExecStart=/home/ubuntu/bannerlord-banner-kings/bot/.venv/bin/python bot.py
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable --now bk-bot
sudo systemctl status bk-bot
journalctl -u bk-bot -f
```

### 5. Nightly clustering cron

```bash
crontab -e
# add:
0 4 * * * cd /home/ubuntu/bannerlord-banner-kings/bot && .venv/bin/python cluster.py >> cluster.log 2>&1
```

### 6. Wiki updates

After editing `docs/WIKI.md`:

```bash
git pull
.venv/bin/python ingest.py
sudo systemctl restart bk-bot
```

## Resource footprint

Steady state on the 12 GB ARM VM:

- bot.py: ~600 MB RAM (model resident).
- chroma_db/: ~5 MB for the wiki.
- bk_bot.sqlite: grows ~1 KB per logged query.

Plenty of headroom on free tier.

## Tuning

All knobs in `.env`:

- `FAQ_HIT_THRESHOLD` (0.85 default) — raise for stricter cache, lower for more cache hits.
- `RAG_TOP_K` (5) — chunks fed to the LLM.
- `CHUNK_TOKENS` (500) / `CHUNK_OVERLAP` (50) — re-run `ingest.py` after changing.
- `CLUSTER_MIN_SIZE` (3) — minimum miss-cluster size to surface as FAQ candidate.
- `CLUSTER_EPS` (0.25) — cosine distance threshold for DBSCAN.

## Extending

- **Source code as corpus**: chunk `BannerKings/**/*.cs` by class/method, embed,
  add to a second Chroma collection, blend results with the wiki at retrieval.
- **Reaction-driven FAQ approval**: replace the cron-posts-text flow with a bot
  listener that watches for ✅ on FAQ-candidate embeds and auto-INSERTs.
- **Per-version answers**: tag chunks with the BK version they apply to and
  filter at query time.
