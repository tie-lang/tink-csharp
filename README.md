# tink-csharp

tink data-flow node frame protocol — C# / .NET class library (no dependencies,
namespace `Tink`). Universal and language-agnostic: any component that obeys
the frame protocol can join a tink pipeline.

```
帧 = [ len: u32 BE ][ payload: len 字节 ][ crc: u32 BE ]
len = payload 字节数
crc = CRC32-IEEE(payload)（多项式 0xEDB88320）
```

Mirrors `std/tink.tie` (tie standard library) and the Rust / C / Python / JS /
C++ / Java tink libraries; pure functions over `ReadOnlySpan<byte>`, IO
(stdin/stdout) left to the caller.

## API (`Tink` static class)

| method | description |
| --- | --- |
| `Crc32(ReadOnlySpan<byte>) -> uint` | CRC32-IEEE over a byte span. Check vector: `Crc32("123456789") == 0xCBF43926` |
| `FrameEncode(ReadOnlySpan<byte>) -> byte[]` | encode a payload into a full frame `[len][payload][crc]` |
| `FrameNext(ReadOnlySpan<byte>, int) -> FrameInfo?` | parse one frame at `pos`, verify CRC; `FrameInfo(Payload, Next)` on success, `null` on out-of-bounds / mismatch |
| `FrameSkip(ReadOnlySpan<byte>, int) -> int?` | skip one frame at `pos` without copying or verifying; `null` on out-of-bounds |

`FrameInfo` is a record holding `byte[] Payload` (a copy) and `int Next`.

## Usage

```csharp
using static Tink.Tink;

var frame = FrameEncode(new byte[] { 1, 2, 3 });
var got = FrameNext(frame, 0); // FrameInfo?
```

## Build & test

```bash
dotnet run                        # runs the bundled unit tests
dotnet build                      # build the Tink library
```

## Cross-language

tink 帧协议各语言实现（API 语义与校验向量一致）：

| language | library |
| --- | --- |
| tie | `std/tink.tie` |
| Rust | `tink-rust`（tink crate） |
| C | `tink-c`（`tink.h` + `tink.c`） |
| Python | `tink-python`（`tink.py`） |
| JavaScript | `tink-js`（`tink.js` + `tink.d.ts`） |
| C++ | `tink-cpp`（`tink.hpp`） |
| Java | `tink-java`（`org.tielang.tink`） |
| C# | this library（`tink-csharp`） |

## License

本仓库使用 **Tie Public License v2.0 (TPL 2.0)**，完整文本见 [LICENSE](LICENSE)。
This repository is distributed under the **Tie Public License v2.0 (TPL 2.0)** — see [LICENSE](LICENSE) for the full text.