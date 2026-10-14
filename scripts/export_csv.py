# -*- coding: utf-8 -*-
"""把 dict/ 下的词典导出成一份 CSV，方便查阅、校对，或提供给官方做本地化。

为什么需要它：
  词典平时是五个按用途拆开的 TSV（BAML 一份、代码串三遍、按方法限定一份），
  机器用起来顺手，人看起来零碎。导出成一张表之后，翻译者可以直接在 Excel
  里筛选、校对、补译，官方要接入也只需要这一份文件。

用法：
    python export_csv.py [输出路径]
    默认输出 ../DisplayDimmer_zh-CN.csv

列含义：
    source       英文原文（\t \n \r \\ 是字面量转义，工具会再反转义一次）
    translation  简体中文译文
    scope        xaml = 界面资源(BAML)；code = 代码里的字符串常量
    pass         第几遍词典（第二遍是拼接碎片，第三遍是补漏，scope 是按方法限定）
    scoped       yes = 只在指定方法里替换，不能全局套用
    method       scoped=yes 时生效的方法名子串
    file         来自哪个词典文件
"""
import csv
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DICT_DIR = os.path.normpath(os.path.join(HERE, '..', 'dict'))

# 顺序即优先级：后面的覆盖前面的（和 build.ps1 里的合并顺序保持一致）
SOURCES = [
    ('us_dict.tsv',  'code', '1'),
    ('us_dict2.tsv', 'code', '2'),
    ('us_dict3.tsv', 'code', '3'),
    ('dict.tsv',     'xaml', '1'),
]


def load(path):
    """读一个词典文件，跳过空行和 # 开头的注释行。"""
    rows = []
    if not os.path.exists(path):
        return rows
    with open(path, encoding='utf-8-sig') as f:
        for line in f:
            line = line.rstrip('\n').rstrip('\r')
            if not line.strip() or line.lstrip().startswith('#'):
                continue
            if '\t' not in line:
                continue
            k, v = line.split('\t', 1)
            rows.append((k, v))
    return rows


def load_scoped(path):
    """读按方法限定的替换表：方法名子串 <TAB> 原文 <TAB> 译文。

    它和主词典的列数不一样（三列 vs 两列），所以要单独解析。
    """
    rows = []
    if not os.path.exists(path):
        return rows
    with open(path, encoding='utf-8-sig') as f:
        for line in f:
            line = line.rstrip('\n').rstrip('\r')
            if not line.strip() or line.lstrip().startswith('#'):
                continue
            parts = line.split('\t')
            if len(parts) < 3:
                continue
            rows.append((parts[0], parts[1], '\t'.join(parts[2:])))
    return rows


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else \
        os.path.normpath(os.path.join(HERE, '..', 'DisplayDimmer_zh-CN.csv'))

    # value = [译文, scope, pass, method, file]
    merged = {}
    counts = {}
    for name, scope, p in SOURCES:
        rows = load(os.path.join(DICT_DIR, name))
        counts[name] = len(rows)
        for k, v in rows:
            merged[k] = [v, scope, p, '', name]

    # 按方法限定的短串（"." " (" 之类）不能进主词典做全局替换，
    # 但它们同样是翻译的一部分，单独列出来并标明只在哪些方法里生效。
    for method, k, v in load_scoped(os.path.join(DICT_DIR, 'us_scope.tsv')):
        if k in merged:
            merged[k][3] = method
        else:
            merged[k] = [v, 'code', 'scope', method, 'us_scope.tsv']
    counts['us_scope.tsv'] = len(load_scoped(os.path.join(DICT_DIR, 'us_scope.tsv')))

    with open(out, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f)
        w.writerow(['source', 'translation', 'scope', 'pass', 'scoped',
                    'method', 'file'])
        for k, v in merged.items():
            w.writerow([k, v[0], v[1], v[2],
                        'yes' if v[3] else '', v[3], v[4]])

    n_xaml = sum(1 for v in merged.values() if v[1] == 'xaml')
    n_code = sum(1 for v in merged.values() if v[1] == 'code')
    n_scoped = sum(1 for v in merged.values() if v[3])

    print('各词典条目数：')
    for name, _, _ in SOURCES:
        print('  %-14s %d' % (name, counts[name]))
    print('  %-14s %d' % ('us_scope.tsv', counts['us_scope.tsv']))
    print()
    print('去重合并后总计 = %d 条' % len(merged))
    print('  xaml（界面资源 BAML）   = %d' % n_xaml)
    print('  code（代码字符串常量）  = %d' % n_code)
    print('  其中按方法限定的        = %d' % n_scoped)
    print()
    print('已导出 -> %s' % out)


if __name__ == '__main__':
    main()
