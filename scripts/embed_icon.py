# -*- coding: utf-8 -*-
"""把多尺寸 .ico 写入 exe 的 PE 资源段（RT_ICON + RT_GROUP_ICON）。

用 kernel32 的 BeginUpdateResource / UpdateResource / EndUpdateResource，
不依赖任何第三方 PE 编辑器。

原理：
  RT_ICON       = 图标图像本体，逐个 ID（1..N）存放 ico 里各尺寸的原始数据
  RT_GROUP_ICON = 图标组目录（GRPICONDIR），告诉 Windows 这些 RT_ICON 属于同一个图标
  两者都挂在中性语言(0)上。
"""
import ctypes
import os
import struct
import sys
from ctypes import wintypes

RT_ICON = 3
RT_GROUP_ICON = 14

k32 = ctypes.WinDLL('kernel32', use_last_error=True)
k32.BeginUpdateResourceW.argtypes = [wintypes.LPCWSTR, wintypes.BOOL]
k32.BeginUpdateResourceW.restype = wintypes.HANDLE
k32.UpdateResourceW.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p,
                                wintypes.WORD, ctypes.c_void_p, wintypes.DWORD]
k32.UpdateResourceW.restype = wintypes.BOOL
k32.EndUpdateResourceW.argtypes = [wintypes.HANDLE, wintypes.BOOL]
k32.EndUpdateResourceW.restype = wintypes.BOOL


def parse_ico(path):
    """解析 ico -> [(w, h, colorcount, planes, bitcount, data_bytes), ...]"""
    with open(path, 'rb') as f:
        blob = f.read()
    reserved, itype, count = struct.unpack('<HHH', blob[:6])
    assert reserved == 0 and itype == 1, '不是 ICO 文件'
    out = []
    for i in range(count):
        off = 6 + i * 16
        (bw, bh, cc, res, planes, bits, size, dataoff) = struct.unpack(
            '<BBBBHHII', blob[off:off + 16])
        data = blob[dataoff:dataoff + size]
        assert len(data) == size, 'ICO 条目数据长度不符'
        out.append((bw, bh, cc, planes, bits, data))
    return out


def embed(exe, ico, group_id=1):
    items = parse_ico(ico)
    print('ICO 条目 %d 个' % len(items))

    h = k32.BeginUpdateResourceW(exe, False)
    if not h:
        raise ctypes.WinError(ctypes.get_last_error())

    ok = True
    try:
        grp_entries = b''
        for i, (bw, bh, cc, planes, bits, data) in enumerate(items, start=1):
            buf = ctypes.create_string_buffer(data, len(data))
            if not k32.UpdateResourceW(h, ctypes.c_void_p(RT_ICON),
                                       ctypes.c_void_p(i), 0,
                                       ctypes.cast(buf, ctypes.c_void_p), len(data)):
                raise ctypes.WinError(ctypes.get_last_error())
            print('  RT_ICON id=%-2d %3dx%-3d %6d 字节' % (
                i, bw or 256, bh or 256, len(data)))
            grp_entries += struct.pack('<BBBBHHIH',
                                       bw, bh, cc, 0, planes, bits, len(data), i)

        grp = struct.pack('<HHH', 0, 1, len(items)) + grp_entries
        gbuf = ctypes.create_string_buffer(grp, len(grp))
        if not k32.UpdateResourceW(h, ctypes.c_void_p(RT_GROUP_ICON),
                                   ctypes.c_void_p(group_id), 0,
                                   ctypes.cast(gbuf, ctypes.c_void_p), len(grp)):
            raise ctypes.WinError(ctypes.get_last_error())
        print('  RT_GROUP_ICON id=%d  %d 字节（含 %d 个条目）'
              % (group_id, len(grp), len(items)))
    except Exception:
        ok = False
        raise
    finally:
        # fDiscard=True 表示失败时放弃改动，保护原文件
        if not k32.EndUpdateResourceW(h, not ok):
            raise ctypes.WinError(ctypes.get_last_error())
    return True


def main():
    exe = sys.argv[1]
    ico = sys.argv[2]
    for p in (exe, ico):
        if not os.path.isfile(p):
            print('!! 找不到:', p)
            return 1
    before = os.path.getsize(exe)
    embed(exe, ico)
    after = os.path.getsize(exe)
    print('完成: %s  %d -> %d 字节 (+%d)' % (exe, before, after, after - before))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
