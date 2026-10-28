# ParkJom 24/7 Hybrid Support Workflow

## 设计目标

ParkJom 采用“预设工作流 + Live Chat”的混合客服模式：

> 已知问题通过预设 Workflow 自动处理或自动开单；未知及非标准问题通过 Live Chat 沟通，必要时由 Admin 手动建立特殊 Ticket。

系统继续保留四种对象：

1. Conversation
2. Support Ticket
3. Operational Incident
4. Dispute / Investigation

“特殊 Ticket”不需要成为第五种对象。它仍然属于 `Support Ticket`，但具有以下属性：

```text
ticketType: Custom
createdBy: Admin
source: LiveChat
```

## 整体入口设计

用户进入 ParkJom Help 页面时，可以选择两个主要入口：

```text
How can we help you?
│
├── Quick Help
│   常见、紧急或有明确处理方式的问题
│   使用预设 Workflow
│
└── Live Chat
    其他问题或需要解释的情况
    与客服对话
```

完整流程：

```text
用户进入 Help Center
          │
          ├──────── Quick Help ────────┐
          │                            │
          │                     选择预设问题
          │                            │
          │                     系统取得业务资料
          │                            │
          │                     执行自动检查
          │                            │
          │              ┌─────────────┼─────────────┐
          │              │             │             │
          │          自动解决     创建 Ticket   创建 Incident/
          │                                         Dispute
          │
          └──────── Live Chat ─────────┐
                                       │
                                  建立 Conversation
                                       │
                             ┌─────────┴─────────┐
                             │                   │
                         直接回答          需要特殊处理
                             │                   │
                      Close Conversation    Admin Create Ticket
                                                 │
                                          Custom Support Ticket
```

## Quick Help：预设 Workflow

Quick Help 不应只是普通表单。每个 Workflow 都需要定义：

```text
需要询问什么
可以自动取得什么资料
要执行哪些系统检查
什么情况可以自动解决
什么情况创建 Ticket
什么情况升级 Incident
什么情况建立 Dispute
分配给哪个团队
使用什么 SLA
```

### Workflow 1：无法进入或离开停车场

用户入口：

```text
I cannot enter or exit
```

系统自动取得：

```text
当前 Booking
停车场
车牌号
付款状态
入口／出口闸门
IoT 在线状态
用户当前位置
```

只询问必要问题：

```text
你无法进入还是离开？
你目前是否被困？
现场是否存在安全风险？
```

处理规则：

```text
如果用户被困或有安全风险
→ Operational Incident P0
→ 同时建立客户 Ticket
→ 立即通知值班人员

如果闸门离线
→ Operational Incident P1
→ 同时建立客户 Ticket
→ Parking Operations

如果闸门在线但 Booking 无法验证
→ Support Ticket High
→ Parking Operations

如果系统可以重新验证 Booking
→ 自动处理
→ 保留 Conversation 记录
```

### Workflow 2：Booking 问题

用户入口：

```text
I have a booking problem
```

问题选项：

```text
Booking not found
Wrong parking location
Cannot cancel
Incorrect booking time
Booking shown as expired
Other
```

处理规则：

```text
系统能够自动修复或解释
→ 显示结果
→ Conversation Closed

需要工作人员修改
→ Support Ticket
→ Customer Support

很多用户同时出现相同错误
→ 建立 Operational Incident
→ 关联相关 Tickets
```

### Workflow 3：Payment 或 Refund 问题

用户入口：

```text
Payment or refund issue
```

问题选项：

```text
Payment successful but booking missing
Refund status
Payment failed
Charged twice
I do not recognize this charge
Other
```

处理规则：

```text
普通退款进度
→ Support Ticket
→ Payments Team

付款成功但 Booking 不存在
→ High Priority Support Ticket
→ Payments Team

重复扣款
→ Dispute / Investigation

用户否认交易
→ Dispute / Investigation
→ 暂停自动退款
→ Finance Review

支付服务大面积失败
→ Operational Incident
```

### Workflow 4：Account、Vehicle 或 Owner 问题

用户入口：

```text
Account, vehicle or owner support
```

问题选项：

```text
Cannot access account
Vehicle information incorrect
Account verification
Owner payout status
Parking listing problem
Owner payout dispute
Other
```

处理规则：

```text
普通账号或车辆问题
→ Support Ticket
→ Customer Support

Owner 付款进度
→ Support Ticket
→ Owner Support

Owner 对付款金额提出异议
→ Dispute / Investigation

账号安全或身份冒用
→ Dispute / Investigation
→ Trust & Safety
```

## Live Chat：处理非标准问题

Live Chat 负责：

- 用户不知道应该选择什么
- 问题不在预设 Workflow
- 用户需要解释复杂情况
- 一般咨询
- 需要多轮沟通
- 查询 Ticket 最新情况

开始 Live Chat 时，系统建立：

```text
Conversation ID
Customer ID
Channel
Started At
Current Booking
Recent Tickets
```

客服可以选择：

```text
Reply and Close
Send Preset Workflow
Create Custom Ticket
Link Existing Ticket
Escalate to Incident
Open Dispute
```

### Admin 手动建立特殊 Ticket

当 Live Chat 无法立即解决时，Admin 点击：

```text
Create Ticket from Conversation
```

系统自动带入：

```text
Customer
Conversation history
Attachments
Booking
Payment
Parking
Vehicle
Contact channel
```

Admin 只需要补充：

```text
Subject
Category
Priority
Assigned Team
Internal summary
Expected follow-up
```

生成的 Ticket 示例：

```json
{
  "ticketType": "Custom",
  "source": "LiveChat",
  "createdByRole": "Admin",
  "conversationId": "CON-2026-00125",
  "status": "Assigned"
}
```

Live Chat 对话不会消失，而是显示：

```text
This conversation continues under Ticket TKT-2026-00382.
You can check its status in My Support Cases.
```

## 四种对象

| 对象 | 建立方式 | 用途 |
| --- | --- | --- |
| Conversation | 每次 Live Chat 或简单咨询 | 可以直接回答，不一定开 Ticket |
| Support Ticket | 预设 Workflow 自动建立，或 Admin 从 Live Chat 建立 | 需要负责人、状态和后续处理 |
| Operational Incident | 紧急 Workflow、系统监控或 Admin 升级 | 用户被困、闸门故障、停车场离线 |
| Dispute / Investigation | 预设付款流程或 Admin 升级 | 重复扣款、付款争议、安全调查 |

对象关系：

```text
Conversation
    │
    ├── 直接解决并关闭
    │
    └── 转成 Support Ticket
                  │
                  ├── 关联 Operational Incident
                  │
                  └── 关联 Dispute / Investigation
```

一个 Ticket 可以关联 Incident 或 Dispute，但它们不能互相取代：

- Ticket：负责与客户沟通
- Incident：负责恢复运营
- Dispute：负责证据、审核和决定

## Ticket 状态

无论由 Workflow 自动建立，还是由 Admin 手动建立，都使用同一套状态：

```text
New
→ Assigned
→ In Progress
→ Waiting for Customer
→ Waiting for Internal Team
→ Resolved
→ Closed
```

特殊情况：

```text
Resolved → Reopened
New → Duplicate
New → Cancelled
```

## 24/7 服务设计

必须区分：

```text
24/7 可以提交问题
≠
24/7 有真人立即回复
```

### 全天自动可用

- Quick Help Workflow
- 自动取得 Booking、Payment 和 IoT 状态
- 自动建立 Ticket
- 自动显示 Ticket 编号
- 自动确认已经收到
- 自动给出预计响应时间
- 自动发送通知
- Live Chat 留言
- 常见问题自动回答

### 紧急问题真人值班

P0／P1 问题必须有真正的 On-call 机制：

```text
Incident Created
       ↓
通知当班人员
       ↓
2 分钟内没有 Acknowledge
       ↓
通知第二值班人
       ↓
5 分钟内仍未确认
       ↓
通知 Supervisor
       ↓
继续升级到 Operations Manager
```

通知方式不应只依赖后台页面：

```text
Push Notification
SMS
电话
Email
内部即时通讯
```

如果没有 24/7 值班人员，ParkJom 不应承诺“24/7 真人即时处理”，而应显示：

```text
紧急停车进出问题提供 24/7 支援。
一般客服问题可全天提交，并会在服务时间内处理。
```

## 用户界面建议

```text
Help & Support

Need immediate assistance?
[ Cannot Enter or Exit ]

Common Issues
[ Booking Problem ]
[ Payment or Refund ]
[ Account, Vehicle or Owner ]

Need help with something else?
[ Start Live Chat ]

Your Cases
TKT-2026-00382    In Progress
TKT-2026-00351    Waiting for Customer
DSP-2026-00018    Under Review
```

如果 Live Chat 没有真人在线：

```text
Our team is currently offline.

You can still send your message.
If follow-up is required, a support ticket will be created
and you will receive a notification.
```

## 推荐落地顺序

1. 建立 Help Center 的 Quick Help 和 Live Chat 两个入口。
2. 建立“无法进入或离开”紧急 Workflow。
3. 建立 Booking Workflow。
4. 建立 Payment／Refund Workflow。
5. 建立 Account／Owner Workflow。
6. 让 Workflow 可以自动建立 Ticket。
7. 让 Admin 可以从 Live Chat 建立 Custom Ticket。
8. 建立 My Support Cases 状态页面。
9. 建立 P0／P1 值班通知和自动升级机制。
10. 最后加入自动分析和 AI。

## 设计总结

```text
已知问题 → 预设 Workflow → 自动处理或自动开单
未知问题 → Live Chat → 直接回答或 Admin 手动开单
紧急问题 → Incident + Ticket
争议问题 → Dispute + Ticket
```

这套模式既提供全天候问题入口，也避免让 Admin 为每个简单问题手动建立 Ticket，同时保留处理特殊情况的弹性。
