#!/usr/bin/env python3
"""
禅净激活码生成。密钥读仓库根 license.secret 或环境变量 CHANJING_LICENSE_SECRET，不写在本文件里。

    python tools/gen_license.py [起始序号] [数量]
"""
import hmac
import hashlib
import os
import sys
from pathlib import Path


def load_secret() -> str:
    env = (os.environ.get("CHANJING_LICENSE_SECRET") or "").strip()
    if env:
        return env
    path = Path(__file__).resolve().parents[1] / "license.secret"
    if path.is_file():
        return path.read_text(encoding="utf-8-sig").strip()
    sys.exit("缺少密钥：在仓库根目录放 license.secret，或设置 CHANJING_LICENSE_SECRET")


SECRET = load_secret()


def gen_key(seq: int) -> str:
    seq_hex = f"{seq:04X}"
    data = f"CJ-{seq_hex}".encode("utf-8")
    sig = hmac.new(SECRET.encode("utf-8"), data, hashlib.sha256).hexdigest()[:6].upper()
    return f"CJ-{seq_hex}-{sig}"


def main():
    start = int(sys.argv[1]) if len(sys.argv) > 1 else 1
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 10
    print(f"禅净激活码（序号 {start} - {start + count - 1}）：")
    print("-" * 40)
    for i in range(start, start + count):
        print(f"  {i:>4}. {gen_key(i)}")
    print("-" * 40)


if __name__ == "__main__":
    main()
