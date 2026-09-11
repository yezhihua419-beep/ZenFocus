import io
path = r"D:\github\chanjing\src\ChanJing.App\Package.appxmanifest"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()
old = '<Resource Language="x-generate"/>'
new = '<Resource Language="en-US"/>\n    <Resource Language="zh-CN"/>'
if old in content:
    content = content.replace(old, new, 1)
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(content)
    print("Done")
else:
    print("Not found")
    for i, line in enumerate(content.split("\n")):
        if "Resource" in line:
            print(f"{i}: {line}")
