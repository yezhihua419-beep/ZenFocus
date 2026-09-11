"""
XAML布局静态检查：检测同一个Grid中是否有重复的Grid.Row（潜在重叠）
用法：python check_xaml_layout.py <xaml文件或目录>
"""
import sys
import re
from pathlib import Path
from xml.etree import ElementTree as ET

def check_file(filepath):
    """检查单个XAML文件的Grid.Row重叠"""
    issues = []
    try:
        with open(filepath, 'r', encoding='utf-8-sig') as f:
            content = f.read()
    except Exception as e:
        return [(filepath, 0, f"无法读取文件: {e}")]

    # 简单的嵌套Grid分析：跟踪Grid的开/闭标签，为每个Grid维护Row集合
    # 用正则匹配 <Grid ...> 和 </Grid>
    grid_stack = []  # 每个元素是 (line_number, row_set)
    current_grid_rows = None

    lines = content.split('\n')
    for i, line in enumerate(lines, 1):
        # 检测Grid开始标签（不包括自闭合）
        if re.search(r'<Grid\b[^>]*>(?![^<]*</Grid>)', line) and not re.search(r'<Grid\b[^>]*/>', line):
            grid_stack.append((i, set()))
        # 检测Grid闭合标签
        elif re.search(r'</Grid>', line):
            if grid_stack:
                start_line, rows = grid_stack.pop()
                if len(rows) != len(set(rows)):
                    from collections import Counter
                    duplicates = {k: v for k, v in Counter(rows).items() if v > 1}
                    issues.append((filepath, start_line, f"Grid(开始于第{start_line}行)有重复Grid.Row: {duplicates}"))
        # 检测Grid.Row属性
        row_match = re.search(r'Grid\.Row="(\d+)"', line)
        if row_match and grid_stack:
            grid_stack[-1][1].add(int(row_match.group(1)))

    return issues

def main():
    if len(sys.argv) < 2:
        # 默认检查src目录下的所有xaml
        paths = [Path('src')]
    else:
        paths = [Path(p) for p in sys.argv[1:]]

    all_issues = []
    xaml_files = []
    for p in paths:
        if p.is_file() and p.suffix == '.xaml':
            xaml_files.append(p)
        elif p.is_dir():
            xaml_files.extend(p.rglob('*.xaml'))

    # 排除bin/obj目录
    xaml_files = [f for f in xaml_files if '\\bin\\' not in str(f) and '\\obj\\' not in str(f)]

    for f in xaml_files:
        issues = check_file(str(f))
        all_issues.extend(issues)

    if all_issues:
        print(f"❌ 发现 {len(all_issues)} 个潜在布局问题：")
        for filepath, line, msg in all_issues:
            print(f"  {filepath}:{line} - {msg}")
        sys.exit(1)
    else:
        print(f"✅ 检查通过：{len(xaml_files)} 个XAML文件，未发现Grid.Row重叠")
        sys.exit(0)

if __name__ == '__main__':
    main()
