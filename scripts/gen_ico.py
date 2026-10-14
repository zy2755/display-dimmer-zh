# -*- coding: utf-8 -*-
"""从 MSIX 包里的图标 PNG 合成一个多尺寸 .ico。

为什么这么做：原 app 是 MSIX/Store 包，exe 自身不含图标资源，
图标全靠 AppxManifest.xml + Images\\*.png。转成绿色版后就没了这层包装，
所以资源管理器和快捷方式都显示空白图标。

小尺寸(16/24/32/48)优先用设计者针对该尺寸优化过的原生 PNG，锐利；
大尺寸从 620x620 的 LargeTile 用 LANCZOS 下采样。
"""
import os
import struct

from PIL import Image

WORK = r'C:\Users\hou33\WorkBuddy\2026-10-13-21-12-56\dd_work'
SRC = os.path.join(WORK, 'portable', 'Images')
OUTDIR = r'D:\DisplayDimmer-中文版'
ICO = os.path.join(OUTDIR, 'Display Dimmer.ico')
PREVIEW = os.path.join(WORK, '_icon_preview.png')

# 目标尺寸 -> 源文件（None 表示按上一档缩放）
PLAN = [
    (16,  'Square44x44Logo.targetsize-16.png'),
    (24,  'Square44x44Logo.targetsize-24.png'),
    (32,  'Square44x44Logo.targetsize-32.png'),
    (48,  'Square44x44Logo.targetsize-48.png'),
    (64,  'Square44x44Logo.scale-200.png'),      # 88x88
    (128, 'Square150x150Logo.scale-200.png'),    # 300x300
    (256, 'LargeTile.scale-200.png'),            # 620x620
]


def load_src(name):
    p = os.path.join(SRC, name)
    im = Image.open(p).convert('RGBA')
    return im


def main():
    entries = []  # (size, png_bytes)
    for size, sname in PLAN:
        im = load_src(sname)
        if im.width != size:
            im = im.resize((size, size), Image.LANCZOS)
        import io
        buf = io.BytesIO()
        im.save(buf, 'PNG', optimize=True)
        entries.append((size, buf.getvalue()))
        print('  %3dx%-3d <- %-40s (源 %dx%d) %d 字节'
              % (size, size, sname, Image.open(os.path.join(SRC, sname)).width,
                 Image.open(os.path.join(SRC, sname)).height, len(buf.getvalue())))
        if size == 256:
            im.save(PREVIEW, 'PNG')

    # 手动拼 ICONDIR + ICONDIRENTRY[] + 各条目数据
    n = len(entries)
    dir_hdr = struct.pack('<HHH', 0, 1, n)          # reserved, type=icon, count
    offset = 6 + 16 * n
    dir_entries = b''
    data_blob = b''
    for size, png in entries:
        bw = 0 if size >= 256 else size             # 256 用 0 表示
        bh = bw
        dir_entries += struct.pack('<BBBBHHII',
                                   bw, bh, 0, 0, 1, 32, len(png), offset)
        data_blob += png
        offset += len(png)

    blob = dir_hdr + dir_entries + data_blob
    with open(ICO, 'wb') as f:
        f.write(blob)
    print('生成 %s  (%d 字节, %d 个尺寸)' % (ICO, len(blob), n))

    # 回读校验
    v = Image.open(ICO)
    print('回读校验: 格式=%s 内含尺寸=%s 主尺寸=%s'
          % (v.format, sorted(v.info.get('sizes', [])), v.size))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
