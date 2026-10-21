#!/usr/bin/env python3
"""Generate a representative oloop data folder for performance tests.

Layout (spec/SPEC.md): <out>/<project>/project.md, <out>/<project>/tasks/<id>.md.
Stdlib only. Deterministic for a given --seed.

Examples:
  scripts/gen-testdata.py /tmp/oloop-perf
  scripts/gen-testdata.py /tmp/oloop-perf --projects 100 --tasks 500 --seed 7
"""
import argparse
import json
import math
import random
import sys
import uuid
from datetime import datetime, timedelta, timezone
from pathlib import Path

TYPES = ["todo", "question", "decision", "feature", "bug"]
TYPE_WEIGHTS = [40, 12, 8, 22, 18]
CLOSED_RATIO = 0.55  # share of closed tasks
LABEL_POOL = [
    "ui", "performance", "spec", "decision", "format", "files", "core", "setup", "test",
    "release", "i18n", "saf", "multi-device", "docs", "refactor", "security", "build",
    "ux", "api", "sync", "backlog", "quick-win", "needs-info", "tera", "demo",
]
ACTORS = ["claude", "alice", "claude", "claude", "anna", "markus"]
WORDS = (
    "task project folder reload parse markdown comment label status update scan list view "
    "filter search group render cache store file write read open close dialog window model "
    "viewmodel binding layout theme button field token config build package release test "
    "fixture schema frontmatter title description detail selection refresh startup sync "
    "conflict backup device user setting path host name type bug feature decision question "
    "todo performance memory latency scroll virtualize measure benchmark profile"
).split()
SENTENCE_STARTS = [
    "Currently", "Needs checking:", "Idea:", "See also", "Observed that", "TODO:",
    "Open point:", "Decision pending:", "Note:", "Repro:",
]


def sentence(rng):
    n = rng.randint(6, 18)
    words = [rng.choice(WORDS) for _ in range(n)]
    if rng.random() < 0.2:
        words[rng.randrange(n)] = f"`{rng.choice(WORDS)}Async`"
    if rng.random() < 0.15:
        words[rng.randrange(n)] = f"{rng.choice(['Views', 'Core', 'ViewModels'])}/{rng.choice(WORDS).title()}.cs:{rng.randint(1, 900)}"
    s = " ".join(words)
    if rng.random() < 0.3:
        s = rng.choice(SENTENCE_STARTS) + " " + s
    return s[0].upper() + s[1:] + "."


def paragraph(rng, sentences):
    return " ".join(sentence(rng) for _ in range(sentences))


def text_of_length(rng, target):
    """Markdown text of roughly `target` chars: paragraphs, bullets, sometimes a code block."""
    parts, size = [], 0
    while size < target:
        r = rng.random()
        if r < 0.55:
            p = paragraph(rng, rng.randint(1, 4))
        elif r < 0.85:
            p = "\n".join(f"- {sentence(rng)}" for _ in range(rng.randint(2, 5)))
        elif r < 0.93:
            p = "```\n" + "\n".join(f"{rng.choice(WORDS)} = {rng.randint(0, 999)}" for _ in range(rng.randint(2, 6))) + "\n```"
        else:
            p = "## " + " ".join(rng.choice(WORDS) for _ in range(rng.randint(2, 4))).title()
        parts.append(p)
        size += len(p) + 2
    text = "\n\n".join(parts)
    return text[:target].rstrip() if target > 40 else text[:target]


def lognormal_len(rng, median, sigma, lo, hi):
    return int(min(hi, max(lo, rng.lognormvariate(math.log(median), sigma))))


def title(rng):
    n = lognormal_len(rng, 45, 0.45, 12, 200)
    t = sentence(rng).rstrip(".")
    while len(t) < n:
        t += " " + rng.choice(WORDS)
    return t[:n].rstrip()


def n_comments(rng, closed):
    # Heavy tail: most tasks have none, a few have many.
    r = rng.random()
    if r < 0.50:
        return 0
    if r < 0.80:
        return rng.randint(1, 2)
    if r < 0.95:
        return rng.randint(3, 6)
    return rng.randint(7, 25 if closed else 15)


def iso(dt):
    return dt.strftime("%Y-%m-%dT%H:%M:%SZ")


def q(s):
    """Quoted YAML scalar. JSON strings are valid YAML double-quoted scalars."""
    return json.dumps(s, ensure_ascii=False)


def block(s, indent):
    pad = " " * indent
    return "\n".join(pad + line if line else "" for line in s.split("\n"))


def make_task(rng, project_id, now, span_days):
    tid = str(uuid.UUID(int=rng.getrandbits(128), version=4))
    ttype = rng.choices(TYPES, TYPE_WEIGHTS)[0]
    closed = rng.random() < CLOSED_RATIO
    created = now - timedelta(seconds=rng.randint(0, span_days * 86400))
    creator = rng.choice(ACTORS)
    labels = rng.sample(LABEL_POOL, rng.choices([0, 1, 2, 3, 4, 6], [20, 30, 25, 15, 7, 3])[0])

    cs, updated, updater = [], created, creator
    for _ in range(n_comments(rng, closed)):
        updated = min(now, updated + timedelta(seconds=rng.randint(60, 5 * 86400)))
        updater = rng.choice(ACTORS)
        body = text_of_length(rng, lognormal_len(rng, 160, 0.9, 15, 3000))
        cs.append((body, updated))
    if closed:
        updated = min(now, updated + timedelta(seconds=rng.randint(60, 10 * 86400)))
        updater = rng.choice(ACTORS)

    # Description: ~8% empty, otherwise lognormal around 300 chars with a long tail.
    desc = "" if rng.random() < 0.08 else text_of_length(rng, lognormal_len(rng, 300, 1.0, 30, 8000))

    lines = [
        "---",
        "formatVersion: 1",
        f"id: {tid}",
        f"projectId: {project_id}",
        f"type: {ttype}",
        f"title: {q(title(rng))}",
        f"status: {'closed' if closed else 'open'}",
        "imagePath: null",
    ]
    lines.append("labels:" + ("" if labels else " []"))
    lines += [f"- {q(l)}" for l in labels]
    lines += [
        f"createdAt: '{iso(created)}'",
        f"createdBy: {q(creator)}",
        f"updatedAt: '{iso(updated)}'",
        f"updatedBy: {q(updater)}",
    ]
    lines.append("comments:" + ("" if cs else " []"))
    for body, at in cs:
        lines.append(f"- body: {q(body)}")
        lines.append(f"  createdAt: '{iso(at)}'")
    lines.append("---")
    return tid, "\n".join(lines) + "\n" + desc + ("\n" if desc else "")


def project_sizes(rng, n, mean_tasks):
    """Skewed task counts per project (lognormal), scaled to the requested mean."""
    raw = [rng.lognormvariate(0, 0.8) for _ in range(n)]
    scale = mean_tasks * n / sum(raw)
    return [max(1, round(r * scale)) for r in raw]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("out", type=Path, help="new data root (must not exist or be empty)")
    ap.add_argument("--projects", type=int, default=50)
    ap.add_argument("--tasks", type=int, default=200, help="mean tasks per project (counts are skewed)")
    ap.add_argument("--days", type=int, default=540, help="creation timestamps spread over this many days")
    ap.add_argument("--seed", type=int, default=42)
    a = ap.parse_args()

    if a.out.exists() and any(a.out.iterdir()):
        sys.exit(f"error: {a.out} exists and is not empty")

    rng = random.Random(a.seed)
    now = datetime.now(timezone.utc).replace(microsecond=0)
    sizes = project_sizes(rng, a.projects, a.tasks)
    total = nbytes = 0

    for i, size in enumerate(sizes):
        pid = str(uuid.UUID(int=rng.getrandbits(128), version=4))
        name = f"perf-project-{i + 1:03d}"
        pdir = a.out / name
        (pdir / "tasks").mkdir(parents=True)
        created = now - timedelta(days=a.days + 1)
        (pdir / "project.md").write_text(
            f"---\nformatVersion: 1\nid: {pid}\nname: {name}\ncreatedAt: '{iso(created)}'\n---\n",
            encoding="utf-8",
        )
        for _ in range(size):
            tid, content = make_task(rng, pid, now, a.days)
            (pdir / "tasks" / f"{tid}.md").write_text(content, encoding="utf-8")
            total += 1
            nbytes += len(content.encode())

    print(f"{a.projects} projects, {total} tasks, {nbytes / 1e6:.1f} MB in {a.out}")
    print(f"tasks per project: min {min(sizes)}, max {max(sizes)}")
    print(f"point the app at it: OLOOP_ROOT={a.out}")


if __name__ == "__main__":
    main()
