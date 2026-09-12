# SyncShootGame

![Unity](https://img.shields.io/badge/Unity-2022.3-black?logo=unity)
![Mirror](https://img.shields.io/badge/Mirror-Networking-blue)
![License](https://img.shields.io/badge/License-Private-red)

> 本科毕业设计项目 · 独立开发

---

## 📖 项目简介

本项目是一款基于 Unity + Mirror 网络框架开发的多人俯视角射击游戏原型，核心研究目标是在弱网环境（高延迟、高丢包）下，通过**客户端预测、服务器和解、插值平滑**三套协同机制，解决传统"服务端权威"模式带来的角色拉扯、操作迟滞等体验问题。

主要代码集中在 `Assets/Scripts/`，网络同步逻辑层为独立实现；`Assets/Mirror/` 为第三方开源框架（详见文末[第三方组件](#-第三方组件)）。

---

## 🎯 核心技术亮点

### 三大同步机制

| 机制 | 作用 | 效果 |
|------|------|------|
| 客户端预测（Client-side Prediction） | 本地即时执行输入，不等服务端回包 | 操作与出弹零延迟生效 |
| 服务器和解（Server Reconciliation） | 收到权威状态后回滚并重演未确认输入 | 消除位置漂移，保证最终一致性 |
| 插值平滑（Interpolation Smoothing） | 本地按 Tick 插值系数渲染，远端做指数平滑 | 消除抖动，无位置跳变 |

### 工程解决的三个具体问题

1. **Host 模式和解位置抖动**：Mirror 环回延迟几乎为 0，对 Host 身份特判跳过和解流程，避免用过时位置把当前预测位置拉回。
2. **Windows 后台帧节流引发 Tick 爆帧**：在 `GameNetworkManager.Awake` 中关闭 VSync 并将帧率锁定为 60，避免非活动窗口降帧时 `TickManager` 在单帧内补偿大量逻辑帧、造成网络消息突发。
3. **线性插值在高抖动下效果差**：远端玩家改用指数平滑逼近目标位置，自适应吸收网络抖动，天然抗消息突发。

---

## 🏗️ 系统架构

```
输入采集 (GatherInput)
    │
    ▼
客户端预测 (ApplyInputLocally)     ←── 本地即时响应
    │  写入 128 格环形输入缓冲
    ▼
发送到服务端 (SendInputToServer：tick + sequence)
    │
    ▼
服务端仲裁 (GameNetworkManager.OnServerTick)
    │  逐 Tick 消费输入队列 → 计算权威位置 → 更新子弹与命中
    ▼
广播权威状态 (ServerStateMessage，含 yourLastProcessedInput)
    │
    ├──► 本地玩家：服务器和解 —— 回滚到权威位置 + 重演未确认输入
    └──► 远端玩家：指数平滑插值渲染
```

**服务端权威**：子弹位置、飞行与命中判定全部由服务端计算，客户端仅负责视觉表现。

---

## 📁 核心文件说明

```
Assets/Scripts/
├── Network/
│   ├── GameNetworkManager.cs    # 网络管理器，服务端仲裁中心（输入队列 / 状态广播 / 子弹管理）
│   └── NetworkMessages.cs       # 网络消息数据结构定义（输入、状态、子弹生成与销毁）
├── Player/
│   └── SyncPlayerController.cs  # 核心同步控制器（预测 / 和解 / 插值）
├── Core/
│   └── TickManager.cs           # 固定逻辑帧管理器（30Hz），提供插值系数
├── UI/
│   └── DebugToggleUI.cs         # 演示开关面板（预测 / 插值实时切换）
├── SimpleBullet.cs              # 客户端子弹视觉表现（沿方向飞行 + 超时自毁）
├── Target.cs                    # 靶子生命值与受击销毁
└── CameraFollow.cs              # 俯视角摄像机跟随（仅跟随位置，不跟随旋转）
```

**历史遗留脚本**（保留以记录演进过程，**不属于当前同步方案**）：
`PlayerController.cs`（早期单机控制）、`NetworkPlayer.cs`（早期 Mirror 移动示例）、`Main.cs`（测试脚本）。当前玩家控制与同步由 `SyncPlayerController` 统一实现。

---

## ⚙️ 弱网测试场景

项目集成 Mirror `LatencySimulation` 传输层，支持实时模拟弱网环境：

| 场景 | Latency | Jitter | 对应 RTT |
|------|---------|--------|---------|
| 理想网络 | 0ms | 0ms | ≈ 0ms |
| 轻度弱网 | 50ms | 20ms | ≈ 100ms |
| 中度弱网 | 100ms | 30ms | ≈ 200ms |
| 重度弱网 | 200ms | 60ms | ≈ 400ms |
| 极端弱网 | 250ms | 80ms | ≈ 500ms |

---

## 🚀 运行方式

### 环境要求

- Unity 2022.3 LTS（本项目使用 2022.3.48f1c1）
- Mirror Networking（已内嵌于项目中，无需额外安装）

### 启动步骤

1. 克隆仓库，用 Unity 打开项目
2. 打开场景 `Assets/Scenes/GameScene`
3. 点击 Play，点击 **Start Host** 启动服务端
4. 构建一个客户端 Build，或使用 ParrelSync 启动第二个实例
5. 客户端点击 **Start Client** 连接

### 演示开关

运行后右上角面板可实时切换：

- **客户端预测**：关闭后本地操作有明显延迟感，出弹也需等服务端回包
- **插值平滑**：关闭后画面出现位置跳变（本地与远端均受影响）

---

## 📊 效果验证

本项目通过运行时可切换的对照实验验证同步效果，结论为**定性验证**：

| 机制 | 验证方式 | 观察结果 |
|------|---------|---------|
| 客户端预测 | 运行时开关预测，对比操作响应 | 开启后本地操作与出弹即时生效，不再等待 RTT |
| 服务器和解 | 在 `LatencySimulation` 下对比是否回滚重演 | 开启后位置连续无回弹；关闭或直接采用权威位置会出现漂移 |
| 插值平滑 | 运行时开关插值，对比画面表现 | 开启后远端移动平滑；关闭后以 30Hz 跳变 |
| Host 特判 | Host 与纯客户端行为对比 | Host 跳过和解，避免环回延迟导致的位置回拉 |

> ⚠️ 上表为行为观察，**尚未接入量化测量工具**。RTT、带宽占用、抖动幅度等具体数值需在补充测量代码后给出，此处不作估算。

---

## ⚠️ 已知不足

1. **环形缓冲溢出会静默丢输入**：客户端输入环形缓冲固定为 128 格。若网络长时间中断导致未确认输入超过 128 个，重演时会因序号不匹配而提前 `break`，超出部分被静默丢弃。后续可改为动态扩容，或在重连时清空缓冲并全量同步。
2. **状态广播未做增量与兴趣管理**：服务端每 Tick 向每个客户端发送全量玩家状态，玩家数量上升后带宽线性增长。可引入增量同步或 Mirror 的 Interest Management。
3. **插值引入约一个 Tick 的显示延迟**：本地插值在前后两个逻辑帧之间渲染，代价是约 33ms（30Hz）的额外延迟。这是为消除抖动而做的取舍，若要进一步降低需引入外推（Extrapolation），但会带来预测错误的风险。
4. **未实现延迟补偿（Lag Compensation）**：射击命中以服务端当前时刻为准，高延迟玩家射击移动目标时会有偏差。可参考 Mirror `LagCompensator` 做历史状态回溯判定。
5. **缺少断线重连与状态恢复**：连接中断后需重新进入，无状态续传机制。

---

## 🛠️ 技术栈

- **引擎**：Unity 2022.3 LTS
- **网络框架**：Mirror + KCP Transport（`KcpTransport` + `LatencySimulation`）
- **同步策略**：状态同步（State Sync）
- **语言**：C#
- **版本控制**：Git / GitHub

---

## 🙏 第三方组件

以下组件为第三方开源项目，**非本人开发**，其许可证文件随源码保留在对应目录中：

| 组件 | 版本 | 位置 | 许可证 |
|------|------|------|--------|
| [Mirror Networking](https://github.com/MirrorNetworking/Mirror) | 96.0.1 | `Assets/Mirror/` | MIT |
| [kcp2k](https://github.com/vis2k/kcp2k) | 随 Mirror 分发 | `Assets/Mirror/Transports/KCP/kcp2k/` | MIT |
| [Mono.Cecil](https://github.com/jbevain/cecil) | 随 Mirror 分发 | `Assets/Mirror/Plugins/Mono.Cecil/` | MIT |
| [SimpleWebTransport](https://github.com/MirrorNetworking/SimpleWebTransport) | 随 Mirror 分发 | `Assets/Mirror/Transports/SimpleWeb/` | MIT |
| BouncyCastle（Mirror Encryption） | 随 Mirror 分发 | `Assets/Mirror/Transports/Encryption/Plugins/` | MIT |

**本项目使用 Mirror 的**：`NetworkBehaviour` / `SyncVar` / `NetworkMessage` / `NetworkManager` 等基础设施，以及 `KcpTransport`、`LatencySimulation` 传输层组件。

**本人独立实现的部分**（位于 `Assets/Scripts/`）：固定逻辑帧调度、客户端预测、服务器和解（回滚重演）、插值平滑、服务端权威子弹与命中判定、运行时调试面板。

---

## 📄 许可说明

本项目为个人毕业设计作品，代码仅用于学习与作品展示。如需引用请联系作者。

---

## 📌 待补充

- [ ] 演示视频 / GIF（预测与插值开关的前后对比）
- [ ] 量化性能数据（RTT、带宽、抖动）
