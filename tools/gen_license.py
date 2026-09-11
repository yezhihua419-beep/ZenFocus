#!/usr/bin/env python3
"""
禅净激活码生成工具（HMAC-SHA256 离线签名）

用法：
    python gen_license.py [起始序号] [数量]

示例：
    python gen_license.py 1 10    # 生成第1-10号激活码
    python gen_license.py 11 5     # 生成第11-15号激活码

激活码格式：CJ-XXXX(序号hex)-XXXXXX(签名hex)
签名 = HMAC-SHA256(密钥, "CJ-" + 序号hex) 前6位大写hex
"""
import hmac
import hashlib
import sys

# ⚠️ 密钥必须与软件端 BlocklistService.LicenseSecret 完全一致
SECRET = "chanjing-zen-focus-2026-v1"


def gen_key(seq: int) -> str:
    """生成指定序号的激活码。"""
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
    print("提示：复制激活码发给用户，用户在软件内输入即可激活。")


if __name__ == "__main__":
    main()
