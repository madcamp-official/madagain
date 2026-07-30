#!/usr/bin/env python3
"""
MindHexer InputPacket UDP 스니퍼 — pos 튀는 값(스파이크) 검증용.

컨트롤러(S10e)가 UDP 45710으로 쏘는 InputPacket(v3, 128바이트, 리틀엔디언)을 디코드해서
Position(x,y,z)을 실시간 관찰한다. 프레임 간 급점프 / NaN·Inf / 패킷 손실을 즉시 표시한다.
WS 페어링과 무관하게 6DoF 스트림만 듣는다(수신 서버·Unity 불필요).

와이어 포맷(PacketSerializer.cs와 1:1):
    off  size  field
      0     4  magic 'MHX2' = 0x3258484D (LE)
      4     1  version (=3)
     12     4  sequence (uint32 LE)
     16     8  timestampMs (int64 LE, 송신측 시계)
     28    12  position x,y,z (float32 LE)
     40    16  rotation x,y,z,w
     56    12  acceleration x,y,z
    128        total

사용:
    python pos_sniffer.py                     # 0.0.0.0:45710 청취, 임계 0.30
    python pos_sniffer.py --threshold 0.15    # 스파이크 판정 임계(연속 프레임 점프, m)
    python pos_sniffer.py --verbose           # 모든 패킷 한 줄씩
    python pos_sniffer.py --port 45710 --bind 192.168.137.1
Ctrl+C로 종료하면 요약 통계를 출력한다.
"""
import argparse
import math
import socket
import struct
import sys
import time

MAGIC = 0x3258484D          # 'MHX2'
PACKET_MIN = 128
OFF_VERSION = 4
OFF_SEQ = 12
OFF_TS = 16
OFF_POS = 28

# 미리 컴파일한 언팩커 (리틀엔디언)
_u32 = struct.Struct("<I")
_i64 = struct.Struct("<q")
_3f = struct.Struct("<3f")


def parse(data: bytes):
    """유효하면 (seq, ts_ms, (x,y,z), version) 반환, 아니면 None."""
    if len(data) < PACKET_MIN:
        return None
    if _u32.unpack_from(data, 0)[0] != MAGIC:
        return None
    version = data[OFF_VERSION]
    seq = _u32.unpack_from(data, OFF_SEQ)[0]
    ts = _i64.unpack_from(data, OFF_TS)[0]
    pos = _3f.unpack_from(data, OFF_POS)
    return seq, ts, pos, version


def dist(a, b):
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def finite(p):
    return all(math.isfinite(c) for c in p)


def main():
    ap = argparse.ArgumentParser(description="MindHexer pos 스파이크 스니퍼")
    ap.add_argument("--port", type=int, default=45710)
    ap.add_argument("--bind", default="0.0.0.0")
    ap.add_argument("--threshold", type=float, default=0.30,
                    help="연속 프레임(seq+1) pos 점프가 이 값(m) 초과면 SPIKE")
    ap.add_argument("--every", type=float, default=0.5, help="요약 출력 간격(초)")
    ap.add_argument("--seconds", type=float, default=0.0,
                    help="지정 시간(초) 후 자동 종료+요약. 0이면 무한(Ctrl+C 종료)")
    ap.add_argument("--verbose", action="store_true", help="모든 패킷 출력")
    args = ap.parse_args()

    # Windows 콘솔(cp949 등)에서 인코딩 못하는 문자가 있어도 죽지 않게. (출력만 대체)
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(errors="replace")
        except Exception:
            pass

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    # 배타 바인딩(Windows): SO_REUSEADDR를 켜면 좀비/중복 인스턴스가 같은 포트에 붙어
    # 도착 패킷을 나눠먹어 "수신 안 됨"처럼 보인다. 두 번째 인스턴스는 명확히 실패하게 둔다.
    if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
        try:
            sock.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        except OSError:
            pass
    try:
        sock.bind((args.bind, args.port))
    except OSError as e:
        print(f"[!] bind {args.bind}:{args.port} 실패 — {e}\n"
              f"    이미 다른 프로그램(수신 서버/Unity)이나 이전에 띄운 스니퍼가 이 포트를 "
              f"쓰고 있습니다. 그 프로세스를 끄거나 --port 를 바꾸세요.\n"
              f"    점유 확인(PowerShell): Get-NetUDPEndpoint | ? LocalPort -eq {args.port}",
              file=sys.stderr)
        return 2
    sock.settimeout(args.every)

    print(f"[*] listening udp {args.bind}:{args.port}  |  spike threshold = {args.threshold} m")
    print(f"[*] 컨트롤러가 이 노트북 IP(192.168.137.1)로 쏘고 있어야 합니다. Ctrl+C 종료.\n")

    sender = None
    prev_pos = None
    prev_seq = None
    total = 0          # 유효 패킷 수
    foreign = 0        # 매직 불일치(타 앱/오염)
    spikes = 0
    losses = 0         # seq 점프(누락) 발생 횟수
    dropped = 0        # 누락된 패킷 총량(추정)
    max_jump = 0.0
    max_jump_seq = -1
    win_count = 0      # 요약 창 내 패킷 수
    win_max_jump = 0.0
    last_summary = time.monotonic()
    last_pos = (0.0, 0.0, 0.0)
    last_seq = -1

    def summary(final=False):
        rate = win_count / max(args.every, 1e-6)
        tag = "FINAL" if final else "----"
        print(f"[{tag}] {rate:6.1f} pkt/s | seq={last_seq} "
              f"pos=({last_pos[0]:+.3f},{last_pos[1]:+.3f},{last_pos[2]:+.3f}) "
              f"| win.maxd={win_max_jump:.3f} | 누적: pkt={total} spike={spikes} "
              f"loss={losses}(~{dropped}p) foreign={foreign} maxd={max_jump:.3f}@seq{max_jump_seq}")

    def report_final():
        print("\n[*] 종료 — 최종 요약:")
        summary(final=True)
        if total:
            print(f"[*] 스파이크 비율: {spikes}/{total} = {100.0*spikes/total:.2f}%  "
                  f"| 최대 점프 {max_jump:.3f} m (seq {max_jump_seq})")

    end_time = time.monotonic() + args.seconds if args.seconds > 0 else None

    try:
        while end_time is None or time.monotonic() < end_time:
            try:
                data, addr = sock.recvfrom(2048)
            except socket.timeout:
                now = time.monotonic()
                if now - last_summary >= args.every:
                    summary()
                    win_count = 0
                    win_max_jump = 0.0
                    last_summary = now
                continue

            p = parse(data)
            if p is None:
                foreign += 1
                continue
            seq, ts, pos, version = p

            if sender is None:
                sender = addr
                print(f"[+] 첫 패킷 수신: {addr[0]}:{addr[1]}  proto v{version}  "
                      f"seq={seq}  pos=({pos[0]:+.3f},{pos[1]:+.3f},{pos[2]:+.3f})\n")

            total += 1
            win_count += 1
            last_pos = pos
            last_seq = seq

            # NaN/Inf → 명백한 튀는 값
            if not finite(pos):
                spikes += 1
                print(f"[NAN ] seq={seq} ts={ts} pos={pos}  ← 비정상(NaN/Inf) 값!")
                prev_pos, prev_seq = None, seq
                continue

            # 시퀀스 갭 = 패킷 손실(스파이크로 오인될 수 있어 따로 표시)
            gap = None
            if prev_seq is not None:
                gap = (seq - prev_seq) & 0xFFFFFFFF
                if gap == 0:
                    # 중복/역순 — 무시하고 계속
                    if args.verbose:
                        print(f"[dup ] seq={seq} (prev={prev_seq})")
                    continue
                if gap > 1:
                    losses += 1
                    dropped += gap - 1
                    print(f"[LOSS] seq {prev_seq}→{seq} ({gap-1}개 누락)")

            # 점프 계산
            if prev_pos is not None:
                jump = dist(pos, prev_pos)
                if jump > max_jump:
                    max_jump, max_jump_seq = jump, seq
                if jump > win_max_jump:
                    win_max_jump = jump

                # 연속 프레임(gap==1)에서만 스파이크로 확정 — 손실 구간은 큰 이동이 정상일 수 있음
                if gap == 1 and jump > args.threshold:
                    spikes += 1
                    dx, dy, dz = (pos[0]-prev_pos[0], pos[1]-prev_pos[1], pos[2]-prev_pos[2])
                    print(f"[SPIKE] seq={seq} |d|={jump:.3f} m  "
                          f"d=({dx:+.3f},{dy:+.3f},{dz:+.3f})  "
                          f"pos=({pos[0]:+.3f},{pos[1]:+.3f},{pos[2]:+.3f})")
                elif args.verbose:
                    print(f"       seq={seq} d={jump:.3f} "
                          f"pos=({pos[0]:+.3f},{pos[1]:+.3f},{pos[2]:+.3f})")

            prev_pos, prev_seq = pos, seq

            now = time.monotonic()
            if now - last_summary >= args.every:
                summary()
                win_count = 0
                win_max_jump = 0.0
                last_summary = now

    except KeyboardInterrupt:
        report_final()
        return 0

    report_final()  # --seconds 만료로 정상 종료
    return 0


if __name__ == "__main__":
    sys.exit(main())
