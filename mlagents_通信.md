这里的端口（port），可以把它理解成：
一台计算机内部，用来区分“网络数据应该交给哪个程序”的编号。

在 ML-Agents 里，它非常关键，因为 Python 训练程序和 Unity 是两个独立进程，它们需要通过本机网络通信。端口就是它们约定的“通信入口”。
先区分 IP 地址和端口
假设有这个地址：
127.0.0.1:5005
可以拆成：
127.0.0.1    :    5005
    ↑                ↑
 IP 地址            端口
哪台电脑？        这台电脑里的哪个服务？
127.0.0.1 或 localhost 都表示当前这台电脑自己。
而 5005 表示这台电脑上的某个通信入口。
可以类比成：
电脑 = 一栋办公楼
IP地址 = 办公楼地址
端口 = 房间号
程序 = 房间里的人
所以：
localhost:5005
基本可以理解成：
“去我这台电脑上的 5005 号通信入口。”

放到 ML-Agents 里看
Python 这边执行：
env = UnityEnvironment(
    file_name="3DBall.exe",
    worker_id=0
)
假设最终选择端口 5005。
Python 的 rpc_communicator.py 会执行类似：
self.server.add_insecure_port("[::]:5005")
self.server.start()
意思可以简化成：
Python：
“我要监听 5005 端口，
如果有程序通过 5005 找我，就把通信交给我的 gRPC 服务处理。”
然后 Python 启动 Unity：
3DBall.exe --mlagents-port 5005
Unity 读取：
--mlagents-port 5005
于是 Unity 知道：
我要连接 localhost:5005
最终关系就是：
┌──────────── Python 进程 ────────────┐
│                                     │
│ RpcCommunicator                     │
│        │                            │
│        ▼                            │
│   监听 5005 端口                    │
└─────────────▲───────────────────────┘
              │
              │ gRPC
              │
              │ localhost:5005
              │
┌─────────────┴───────────────────────┐
│        Unity 进程                   │
│                                     │
│ RpcCommunicator.cs                  │
│                                     │
│ Channel("localhost:5005")           │
└─────────────────────────────────────┘
注意一个很重要的点：端口不是一根真实存在的线，也不是某个硬件接口。 5005 只是操作系统网络栈管理的一个逻辑编号。
为什么还需要 worker_id？
如果同时运行两个 Unity 环境，就不能让两个 Python 通信服务都占用同一个端口。
例如：
env0 = UnityEnvironment(
    file_name="3DBall.exe",
    worker_id=0
)

env1 = UnityEnvironment(
    file_name="3DBall.exe",
    worker_id=1
)
可以形成：
Python / Unity #0
        │
        └──── 5005

Python / Unity #1
        │
        └──── 5006
这样操作系统就知道：
发往 5005 的数据 → 第一组通信

发往 5006 的数据 → 第二组通信
因此 worker_id 不是 Agent ID。它主要用于让不同 Unity 环境实例使用不同的通信端口。
而且一个端口对应的 Unity 实例内部仍然可以有很多 Agent：
                 Python
                   │
             localhost:5005
                   │
                 Unity
                   │
        ┌──────────┼──────────┐
        ▼          ▼          ▼
      Agent 1    Agent 2    Agent 3
所以这里最好建立三个层次：
端口
  ↓
Python ↔ Unity进程 的通信通道

Unity进程
  ↓
运行一个场景

场景
  ↓
可以包含多个 Agent
这也是为什么你看到：
m_Channel = new Channel(
    $"localhost:{port}",
    ChannelCredentials.Insecure
);
时，可以直接读成：
Unity 创建一个 gRPC 通信通道，目标是本机上指定端口的 Python 服务。

而 Python 的：
self.server.add_insecure_port("[::]:" + str(self.port))
可以读成：
Python 把自己的 gRPC 服务绑定到这个端口，等待 Unity 连接。

如果你继续往底层学，下一步最值得搞清楚的是 “监听端口 → TCP socket → gRPC”这三层到底是什么关系。理解这一层以后，RpcCommunicator 的代码会清晰很多。








今天 14:38
为什么两个程序都在本机，还要通过网络通信













因为**“在同一台电脑上”不等于“在同一个进程、同一块可直接访问的内存里”**。
在 ML-Agents 中，Python 和 Unity 通常是两个独立进程：
一台电脑
│
├── Python 进程
│   ├── Python代码
│   ├── PPO/SAC
│   └── UnityEnvironment
│
└── Unity 进程
    ├── C#代码
    ├── 物理引擎
    └── Agent
操作系统会把不同进程的内存隔离开。因此 Python 不能简单地写：
ball_position = unity.ball.position
直接读取 Unity 进程内存里的 ball.position。
反过来，Unity 也不能直接访问：
Python里的numpy数组
Python里的神经网络对象
Python里的变量
所以两个进程必须使用某种**进程间通信 IPC（Inter-Process Communication）**机制。
网络通信只是 IPC 的一种。
为什么 ML-Agents 偏偏选择网络通信？
因为它非常适合这种架构。
操作系统本身就允许：
进程 A
   │
   │ TCP / gRPC
   ▼
localhost:5005
   ▲
   │
进程 B
即使数据根本没有离开你的电脑，也完全可以使用 TCP/IP。
这里的“网络通信”不要理解成：
Python
   ↓
路由器
   ↓
互联网
   ↓
Unity
实际上 ML-Agents 使用 localhost 时更接近：
Python
   │
   ▼
操作系统网络栈
   │
   ▼
Unity
数据不会为了通信特意跑到互联网再回来。
例如 Unity：
new Channel("localhost:5005", ...)
其中：
localhost
明确表示：
我要连接这台机器自己。

为什么不直接使用共享内存？
理论上完全可以。
两个进程的通信方式很多，例如：
进程间通信 IPC
│
├── Pipe 管道
├── Shared Memory 共享内存
├── Unix Domain Socket
├── TCP Socket       ← ML-Agents底层涉及这一类
└── ...
ML-Agents 在上面又使用了 gRPC + Protocol Buffers。
这样做虽然比直接共享内存多了一些通信开销，但工程上有明显优势：Python 和 C# 不需要理解彼此的内存对象。
例如 Unity 有：
Vector3 position;
float reward;
bool done;
Python 不可能直接把 C# 的 Vector3 对象当成 Python 对象。
双方可以约定一个消息格式：
AgentInfo
----------------
position
reward
done
agent_id
...
Unity：
C#对象
   ↓
Protocol Buffers消息
   ↓
gRPC
   ↓
localhost:5005
Python：
localhost:5005
   ↓
gRPC
   ↓
Protocol Buffers消息
   ↓
Python / NumPy数据
反方向发送动作也是一样：
Python神经网络
     │
     ▼
action = [0.3, -0.8]
     │
     ▼
 protobuf / gRPC
     │
     ▼
Unity
     │
     ▼
Agent执行动作
这就把**“Unity 怎么实现物理模拟”和“Python 怎么实现强化学习”**解耦了。
这也解释了为什么要有端口
现在你可以把前面几个概念串起来：
                 同一台电脑
┌─────────────────────────────────────────┐
│                                         │
│   Python进程                  Unity进程  │
│                                         │
│   PPO                            Agent   │
│    │                              ▲     │
│    ▼                              │     │
│ UnityEnvironment                  │     │
│    │                              │     │
│ RpcCommunicator      RpcCommunicator.cs │
│    │                       │             │
│    └──── localhost:5005 ───┘             │
│                 ▲                       │
│                 │                       │
│              操作系统                   │
│              网络栈                     │
│                                         │
└─────────────────────────────────────────┘
其中各个概念分别回答不同的问题：
localhost → 跟哪台电脑通信？
5005      → 跟这台电脑上的哪个通信入口通信？
gRPC      → 双方怎么调用和交换消息？
protobuf  → 消息按照什么格式组织？
而最根本的原因是：
Python 和 Unity 是两个独立进程，不能像同一个程序内部的两个函数那样直接共享普通变量，所以需要 IPC。ML-Agents 选择了基于 localhost 的 gRPC 通信方案。

如果 Python 和 Unity 真的是同一个进程，那么确实就没有必要为了普通函数调用专门走 localhost:5005 这一套了。




Protocol Buffers 消息（protobuf message）可以理解成：按照双方事先约定的数据结构组织起来的一份结构化数据。
它解决的核心问题是：
Unity 和 Python 要交换 observation、reward、agent_id、action 等数据，双方怎么知道收到的一串数据分别代表什么？

3. 为什么还要“序列化”？
内存中的对象不能直接通过 TCP 发送。
例如 Unity 内存里可能有：
C# AgentInfo对象
┌──────────────────┐
│ agent_id = 7     │
│ reward   = 0.8   │
│ done     = false │
└──────────────────┘
网络底层最终传输的是：
byte byte byte byte ...
因此需要：
结构化对象
     │
     │ protobuf 序列化
     ▼
一串 bytes
     │
     │ TCP传输
     ▼
一串 bytes
     │
     │ protobuf 反序列化
     ▼
结构化对象
这个过程叫：
Serialization（序列化）
反方向叫：
Deserialization（反序列化）。


Python 和 Unity 是两个独立进程，不能像同一个程序内部的两个函数那样直接共享普通变量，所以需要 IPC。ML-Agents 选择了基于 localhost 的 gRPC 通信方案。















gRPC 可以先理解成：让一个程序像“调用函数”一样，请求另一个程序做事情并返回结果的通信框架。
在 ML-Agents 中，它主要负责 Unity 进程和 Python 进程之间的通信组织。
你已经知道 protobuf、TCP、Socket，所以现在正好可以把 gRPC 放进去。
先看为什么需要 gRPC
假设没有 gRPC。
Unity 想把：
Agent ID = 7
reward = 0.8
observation = [...]
发送给 Python。
你当然可以自己直接使用 TCP Socket：
Unity
  │
  │ protobuf序列化
  ▼
bytes
  │
  │ TCP Socket
  ▼
Python
但马上会出现很多工程问题：
这条消息是什么类型？
一条消息从哪里开始、哪里结束？
这是请求还是响应？
收到消息之后调用 Python 的哪个函数？
发生错误怎么办？
如何同时处理多个请求？
如果全部自己实现，你实际上是在自己设计一套通信协议。
gRPC 就把大量这类工作封装起来了。
1. RPC 是什么？
理解 gRPC，首先理解 RPC：Remote Procedure Call，远程过程调用。
普通函数调用：
result = add(10, 20)
发生在：
Python程序
   │
   ├── add(10,20)
   │
   ▼
Python函数
   │
   ▼
return 30
但是假设 add() 实际运行在另一个进程甚至另一台电脑。
RPC 希望让你的代码仍然呈现出类似：
程序A
  │
  │ 调用 Add(10,20)
  ▼
程序B
  │
  │ 执行
  ▼
返回 30
实际上背后发生的是：
程序A
  │
  │ 把参数编码
  ▼
网络请求
  │
  ▼
程序B
  │
  │ 调用真正的函数
  ▼
结果
  │
  │ 编码
  ▼
网络响应
  │
  ▼
程序A
所以：
RPC 的核心思想，是把跨进程/跨机器通信抽象成“调用远程方法”。

2. gRPC 就是一套 RPC 框架
gRPC 帮你实现这套机制。
例如我们定义一个服务：
service Calculator {
    rpc Add(AddRequest) returns (AddResponse);
}
同时定义数据：
message AddRequest {
    int32 a = 1;
    int32 b = 2;
}

message AddResponse {
    int32 result = 1;
}
这里已经出现两个东西：
protobuf message
      ↓
定义数据

gRPC service / rpc
      ↓
定义可以调用什么方法
于是客户端可以概念性地调用：
Add(
    a = 10,
    b = 20
)
gRPC 背后负责：
10, 20
  ↓
protobuf序列化
  ↓
gRPC请求
  ↓
HTTP/2
  ↓
TCP
  ↓
服务器
服务器处理完成：
30
 ↓
protobuf序列化
 ↓
gRPC响应
 ↓
HTTP/2
 ↓
TCP
 ↓
客户端
3. 所以 protobuf 和 gRPC 不是一个东西
这是你现在最应该分清楚的地方。
protobuf 主要管数据：
message AgentInfo {
    int32 agent_id = 1;
    float reward = 2;
}
回答：
数据有什么字段？字段是什么类型？怎么序列化？

而 gRPC 主要管调用：
service UnityToExternalProto {
    rpc Exchange(...) returns (...);
}
回答：
客户端可以调用服务器的什么 RPC？请求是什么？响应是什么？

可以简单记：
protobuf = “信里面写什么，以及怎么编码”

gRPC     = “怎么组织这次请求和响应”
4. 再往下面才是 HTTP/2、TCP 和 Socket
完整关系：
┌────────────────────────────┐
│       ML-Agents            │
│ observation/action/reward  │
└─────────────┬──────────────┘
              ▼
┌────────────────────────────┐
│ protobuf                   │
│ 定义数据结构 + 序列化      │
└─────────────┬──────────────┘
              ▼
┌────────────────────────────┐
│ gRPC                       │
│ RPC调用机制                │
└─────────────┬──────────────┘
              ▼
┌────────────────────────────┐
│ HTTP/2                     │
│ gRPC的传输承载协议         │
└─────────────┬──────────────┘
              ▼
┌────────────────────────────┐
│ TCP                        │
│ 可靠、有序的字节流         │
└─────────────┬──────────────┘
              ▼
        Socket API
              │
              ▼
        操作系统网络栈
注意，Socket 严格来说不是一个像 TCP 那样的协议层，而是程序使用操作系统网络能力的 API/通信端点。
5. 回到 ML-Agents
你之前看到 Python：
self.server = grpc.server(...)
就是：
Python 创建一个 gRPC Server。

然后：
self.server.add_insecure_port(...)
让这个 gRPC Server 在指定地址/端口提供服务。
例如：
:5005
Unity 这边：
m_Channel = new Channel(
    $"localhost:{port}",
    ChannelCredentials.Insecure
);
创建到：
localhost:5005
的 gRPC Channel。
然后：
m_Client =
    new UnityToExternalProto
        .UnityToExternalProtoClient(m_Channel);
这里非常关键。
Unity 创建的是：
gRPC 客户端对象

之后 Unity 可以通过它调用 Python 暴露出来的 RPC。
所以架构是：
Unity                              Python
进程                               进程

ML-Agents C#                       ML-Agents Python
     │                                   │
     ▼                                   ▼
gRPC Client                        gRPC Server
     │                                   │
     └──────── localhost:5005 ───────────┘
                  │
               HTTP/2
                  │
                 TCP
6. 一个容易产生的误解
看到：
Unity → gRPC → Python
容易认为：
“Unity 把 gRPC 发给 Python。”

严格来说这不太准确。
gRPC 不是某种数据本身。
更准确的是：
Unity 和 Python 使用 gRPC 框架进行 RPC 通信。

真正在线路上传输的最终还是经过编码后的字节。
所以：
Agent observation
        ↓
构造成 protobuf message
        ↓
protobuf 序列化
        ↓
gRPC组织RPC请求
        ↓
HTTP/2
        ↓
TCP
        ↓
字节传输
Python 收到后再逐层恢复。
7. 最后用一句话区分你目前学到的全部概念
IP
“发给哪个网络接口？”

Port
“交给哪个网络服务入口？”

Socket
“程序通过什么接口/端点使用网络？”

TCP
“字节怎样可靠、有序地传过去？”

HTTP/2
“如何承载和组织 gRPC 的通信？”

protobuf
“结构化数据怎么定义和编码？”

gRPC
“两个程序怎么进行远程方法调用？”

ML-Agents
“具体交换 observation、action、reward 做什么？”
对于你现在看的 ML-Agents 源码，最关键的一句话是：
Unity 是 gRPC 客户端，Python 是 gRPC 服务端；双方使用 protobuf 定义和编码消息，通过 gRPC 的 RPC 请求/响应进行通信，而 gRPC 的底层通信最终建立在 HTTP/2、TCP 和 Socket 之上。