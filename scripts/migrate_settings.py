# -*- coding: utf-8 -*-
"""把配置里已持久化的「界面串」同步迁移成中文。

为什么必须做：应用把一部分**界面文案本身**当作数据存了下来 ——
例如 settings.json 里的
    "SelectedAction": "Brightness up"
    "SelectedTarget": "All Displays"
翻译 DLL 后，代码里比较/查找的字符串都变成了中文，而磁盘上还是英文，
下拉框会取不到项、热键动作会认不出来。
所以这里对配置做一次同口径的替换。

只做**整串精确匹配**替换：宁可漏改（用户重新选一次即可），
不能误伤（比如把设备名、监视器型号里恰好的英文词换掉）。
"""
import json
import os
import re
import shutil
import sys

APP = os.path.join(os.environ['APPDATA'], 'DisplayDimmer')


def load_map(paths):
    m = {}
    for p in paths:
        if not os.path.exists(p):
            continue
        for line in open(p, encoding='utf-8'):
            line = line.rstrip('\n')
            if not line or line.startswith('#') or '\t' not in line:
                continue
            k, v = line.split('\t', 1)
            if k:
                m[k] = v
    return m


def convert(obj, m, hits):
    if isinstance(obj, dict):
        return dict((k, convert(v, m, hits)) for k, v in obj.items())
    if isinstance(obj, list):
        return [convert(v, m, hits) for v in obj]
    if isinstance(obj, str) and obj in m:
        hits.append(obj)
        return m[obj]
    return obj


def main():
    m = load_map(sys.argv[1:])
    print('词典合计 %d 条' % len(m))

    targets = ['settings.json', 'settings.json.bak', 'DisplayDdcHealth.json']
    for name in targets:
        p = os.path.join(APP, name)
        if not os.path.exists(p):
            print('跳过（不存在）:', name)
            continue
        raw = open(p, encoding='utf-8-sig').read()
        try:
            data = json.loads(raw)
        except Exception as e:
            print('跳过（不是 JSON）:', name, e)
            continue
        hits = []
        new = convert(data, m, hits)
        if not hits:
            print('无需迁移:', name)
            continue
        bak = p + '.pre-zh'
        if not os.path.exists(bak):
            shutil.copy2(p, bak)
        with open(p, 'w', encoding='utf-8') as f:
            json.dump(new, f, ensure_ascii=False, indent=2)
        print('%s: 迁移 %d 处 -> %s' % (name, len(hits), sorted(set(hits))))
        print('   备份: ' + bak)


if __name__ == '__main__':
    main()
