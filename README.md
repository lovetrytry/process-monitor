# 进程监控 Process Monitoring

## 介绍 Introduction

Windows 端的进程监控工具，可以监控进程的 CPU、内存、磁盘等使用率和使用时间等信息。

## 功能 Features

* 每分钟统计一次过去一分钟内进程的使用率和使用时间等信息。
* 分为以下几类：
  * ✅️CPU 使用率
  * ✅️CPU 使用时间
  * ✅️内存使用率
  * ✅️内存使用时间
  * ✅️磁盘使用率
  * ✅️磁盘使用时间
  * 网络使用率
  * 网络使用时间
  * GPU 使用率
  * GPU 使用时间
* 每类都倒序排序，取前 10 个。
* 每个进程需要获取进程名、进程位置（绝对路径）。
* 获取的信息保存到文件中，然后可以在界面上进行查看。

## 编程语言 Programming Language

* C#，.NET 8.0

## UI

* 使用 WPF，界面可以参考截图。

## 项目结构 Project Structure

* ProcessMonitor.Core：核心逻辑，获取进程信息。
* ProcessMonitor.UI：UI 界面。
