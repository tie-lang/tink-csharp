# tink-csharp

tink v3 frame skeleton — C# / .NET class library (no dependencies, namespace
`Tink`). Universal and language-agnostic: any component that obeys the tink v3
frame protocol can join a tink pipeline.

```
帧（16 字节定长头，大端）：
  [magic 0x74 0x6B][ver=3][type=0x04][flags u16][stream_id u32][seq u32][len u16]
  [扩展区：ext_len u16 + TLV]?     (flags.EXT)
  [载荷：len 字节]                 (flags.OFFSET：前 4B = u32 BE offset)
  [尾部：CRC32C 4B | AEAD tag 16B | 无]
flags：1=EXT 2=PROTECTED 4=CRC 8=FIN 16=OFFSET 32=URGENT
```

实现子集（与 C / Python / Rust / Java 移植面一致）：CRC32C 尾部、EXT 区、
≤65535B 单帧、消息分片（>65531B 拆为 OFFSET+FIN 分片，seq = 片序）与解码侧
透明顺序重组；PROTECTED 帧拒收（密码层不在本层范围）。v1/v2 线格式已断代
移除——旧帧在 v3 解码路径下拒收。

## API (`TinkV3` static class)

| method | description |
| --- | --- |
| `Crc32C(ReadOnlySpan<byte>) -> uint` | CRC32C（Castagnoli，反射多项式 0x82F63B78）。Check vector: `Crc32C("123456789") == 0xE3069283` |
| `FrameEncode(ReadOnlySpan<byte>) -> byte[]` | 单帧编码（CRC\|FIN；载荷 ≤ 65535） |
| `FrameEncodeExt(ReadOnlySpan<byte>, ReadOnlySpan<byte>) -> byte[]` | 携带扩展区（原始 TLV 字节）的单帧 |
| `MsgEncode(ReadOnlySpan<byte>) -> byte[]` | 消息层：载荷 ≤ 65531 单帧；否则 OFFSET+FIN 分片序列（末片 FIN） |
| `MsgDecode(ReadOnlySpan<byte>, int) -> Decoded?` | 解一条逻辑消息（透明重组分片）；malformed / CRC 不符 / 分片链断裂 → `null` |

`Decoded` is a record holding `byte[] Payload` (a copy) and `int Next`.

## Usage

```csharp
using static Tink.TinkV3;

var frame = MsgEncode(new byte[] { 1, 2, 3 });
var got = MsgDecode(frame, 0); // Decoded?
```

## Build & test

```bash
dotnet run                        # runs the bundled KAT tests (16 checks)
dotnet build                      # build the Tink library
```

## Cross-language

tink v3 帧协议各语言实现（API 语义与 KAT 向量逐字节一致）：

| language | library |
| --- | --- |
| tie | `tlib std/tink.tie`（参考实现） |
| C | `tink-c`（`tink_v3.h` + `tink_v3.c`） |
| Rust | `tink-rust`（`src/v3.rs`） |
| Python | `tink-python`（`tink_v3.py`） |
| Java | `tink-java`（`TinkV3.java`） |
| C# | this library（`TinkV3.cs`） |

## License

本仓库使用 **Tie Public License v2.3 (TPL 2.3)**，完整文本见 [LICENSE](LICENSE)。
This repository is distributed under the **Tie Public License v2.3 (TPL 2.3)** — see [LICENSE](LICENSE) for the full text.
