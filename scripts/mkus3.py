# -*- coding: utf-8 -*-
"""第三遍：第一遍的「类白名单」漏掉的界面类，以及少数带首尾空格/含换行的碎片。

漏掉的类：ProDialogHeader、WhatsChangedParser（离线更新说明）、
AutomationPresetPreviewPolicy、MainWindow.ConfigureBrightnessSafetyHintVisuals 等。
"""
import sys

M = {
    # ---- 专业版弹窗头部（ProDialogHeader）----
    'One-time purchase. ': '一次性购买。',
    'No subscription.': '无需订阅。',
    'Restore Pro': '恢复专业版',

    # ---- 版本更新说明（联网失败时的兜底文案）----
    "What's changed in version ": '版本更新内容 ',
    'Version 2.2.12': '版本 2.2.12',
    'This update adds saved Pro presets, compact automation controls, and smoother everyday brightness control.':
        '本次更新新增了「已保存的专业版预设」、更紧凑的自动化控件，以及更顺手的日常亮度控制。',
    'Save your favorite brightness, contrast, and temperature combinations as Pro presets.':
        '把你常用的亮度、对比度和色温组合保存为专业版预设。',
    'Apply presets from the main window, tray menu, hotkeys, schedules, app rules, or command line.':
        '可从主窗口、托盘菜单、热键、计划任务、应用规则或命令行调用预设。',
    'Browse revamped Schedules, App Rules and Hotkeys as compact cards that expand for editing, with more responsive Settings as your lists grow.':
        '全新的计划任务、应用规则和热键改为紧凑卡片，需要编辑时展开；列表变长后设置界面也更跟手。',
    'Scroll up or down over the tray icon to raise or lower brightness in 5% steps, using the selection in the main window.':
        '在托盘图标上滚动滚轮，即可按主窗口中选择的目标，以 5% 为步长调高或调低亮度。',
    'Optionally show the hidden main window after selected brightness changes, without taking focus, for a duration you choose.':
        '可选：在指定的亮度发生变化后短暂显示已隐藏的主窗口（不抢焦点），时长由你决定。',

    # ---- 预设预览（AutomationPresetPreviewPolicy）----
    'Brightness ': '亮度 ',
    'Contrast ': '对比度 ',
    ' saved targets cannot be used. Edit preset to review.':
        ' 个已保存的目标不可用。请编辑预设进行检查。',
    '1 saved target cannot be used. Edit preset to review.':
        '1 个已保存的目标不可用。请编辑预设进行检查。',
    'No displays included. Edit this preset before applying.':
        '未包含任何显示器。应用前请先编辑此预设。',
    'No usable saved display targets. Edit this preset before applying.':
        '没有可用的已保存显示器目标。应用前请先编辑此预设。',
    'This older preset has no saved display targets. Choose another before applying.':
        '此旧版预设没有已保存的显示器目标。应用前请另选一个。',
    ' more saved ': ' 台其他已保存的 ',

    # ---- 亮度安全提示 / 短暂反馈 ----
    ' at this brightness level. Reverting in ': '（当前亮度下可能看不清）。将在 ',
    ' seconds.': ' 秒后还原。',
    ' second.': ' 秒后还原。',
    ' seconds · ': ' 秒 · ',
    ' second · ': ' 秒 · ',
    ' sources': ' 个来源',
    ' and ': ' 和 ',

    # ---- 设置窗口 状态/提示 ----
    'Next schedule at ': '下一计划任务时间：',
    'Schedule active until ': '计划任务生效至 ',
    'Paused schedules: ': '已暂停的计划任务：',
    'Preset requested on ': '已请求预设，目标：',
    'Used by ': '被以下使用：',
    ' for enabled displays — ': '（已启用的显示器）— ',
    ' on the keyboard. Press Esc to cancel.': ' 键。按 Esc 取消。',
    ' Resume automation releases app-rule, schedule and manual temperature holds for this scope and reevaluates the current rules.':
        '「恢复自动化」会解除此范围内的应用规则、计划任务和手动色温挂起，并重新评估当前规则。',
    ' app rule': ' 条应用规则',
    ' app rules': ' 条应用规则',
    'Preview it free for ': '可免费预览 ',
    'Unlock Pro to switch back to ': '解锁专业版即可切回 ',
    'Your next free preview is available in ': '下次免费预览将在 ',

    # ---- 支持邮件正文 ----
    'Hi, I need help activating or restoring my direct Display Dimmer Pro license.\\r\\n\\r\\nApp version: ':
        '你好，我在激活或恢复直接购买的 Display Dimmer 专业版许可证时需要帮助。\\r\\n\\r\\n应用版本：',
    '\\r\\nLocation: ': '\\r\\n购买地点：',
    '\\r\\n\\r\\nPlease do not include your full license key in this email.':
        '\\r\\n\\r\\n请不要在邮件中包含完整的许可证密钥。',
    'No email app could be opened. The support address was copied:\\r\\n\\r\\nsupport@displaydimmer.com':
        '无法打开邮件应用。已复制支持邮箱地址：\\r\\n\\r\\nsupport@displaydimmer.com',
    'App version: ': '应用版本：',
    'Store error: ': '商店错误：',
    'Store result: ': '商店结果：',
    'Purchase location: ': '购买地点：',
    'Purchase window time: ': '购买时间：',
    'A valid HTTPS Lemon Squeezy checkout URL is required.':
        '需要有效的 HTTPS Lemon Squeezy 结账链接。',
}


def main():
    dst = sys.argv[1] if len(sys.argv) > 1 else 'us_dict3.tsv'
    with open(dst, 'w', encoding='utf-8', newline='\n') as f:
        for k, v in M.items():
            f.write(k + '\t' + v + '\n')
    print('第三遍条目 =', len(M), '->', dst)


if __name__ == '__main__':
    main()
