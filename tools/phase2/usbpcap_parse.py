#!/usr/bin/env python3
"""
Parse a USBPcap (DLT 249) pcap and extract interrupt IN payloads for one USB device.

Usage:
  python usbpcap_parse.py capture.pcap --device 3 --endpoint 0x81
  python usbpcap_parse.py capture.pcap --device 3 --endpoint 0x81 --summary

USBPcap capture header (little-endian, packed, 27 bytes):
  uint16 headerLen
  uint64 irpId
  uint32 status
  uint16 function
  uint8  info
  uint16 bus
  uint16 device
  uint8  endpoint
  uint8  transfer
  uint32 dataLength

This tool only reads the pcap. It never touches the device.
"""
import argparse
import struct
import sys
from collections import OrderedDict, defaultdict

PCAP_MAGIC_LE = 0xA1B2C3D4
PCAP_MAGIC_BE = 0xD4C3B2A1
USBPCAP_DLT = 249
TRANSFER_INTERRUPT = 1


def read_pcap(path):
    with open(path, "rb") as f:
        gh = f.read(24)
        if len(gh) != 24:
            raise ValueError("short pcap global header")
        magic = struct.unpack("<I", gh[:4])[0]
        if magic == PCAP_MAGIC_LE:
            endian = "<"
        elif magic == PCAP_MAGIC_BE:
            endian = ">"
        else:
            raise ValueError("not a classic pcap file (magic=0x%08X)" % magic)
        version_major, version_minor, thiszone, sigfigs, snaplen, network = struct.unpack(
            endian + "HHiIII", gh[4:24]
        )
        if network != USBPCAP_DLT:
            raise ValueError("link type %d is not USBPcap (249)" % network)
        while True:
            rh = f.read(16)
            if len(rh) < 16:
                break
            ts_sec, ts_usec, incl_len, orig_len = struct.unpack(endian + "IIII", rh)
            data = f.read(incl_len)
            if len(data) < incl_len:
                break
            yield ts_sec, ts_usec, data


def parse_usbpcap(data):
    if len(data) < 27:
        return None
    header_len, irp_id, status, function, info, bus, device, endpoint, transfer, data_length = struct.unpack(
        "<HQIHBHHBBI", data[:27]
    )
    if header_len < 27 or header_len > len(data):
        return None
    payload = data[header_len:header_len + data_length]
    return {
        "header_len": header_len,
        "irp_id": irp_id,
        "status": status,
        "function": function,
        "info": info,
        "bus": bus,
        "device": device,
        "endpoint": endpoint,
        "transfer": transfer,
        "data_length": data_length,
        "payload": payload,
    }


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pcap")
    ap.add_argument("--device", type=int, required=True, help="USB device address (e.g. 3)")
    ap.add_argument("--bus", type=int, default=None, help="USB bus/root hub number (optional)")
    ap.add_argument("--endpoint", type=lambda s: int(s, 0), default=0x81,
                    help="endpoint address including direction bit (default 0x81)")
    ap.add_argument("--summary", action="store_true", help="group identical payloads")
    args = ap.parse_args()

    packets = []
    t0 = None
    for ts_sec, ts_usec, data in read_pcap(args.pcap):
        p = parse_usbpcap(data)
        if p is None:
            continue
        if p["transfer"] != TRANSFER_INTERRUPT:
            continue
        if p["device"] != args.device:
            continue
        if args.bus is not None and p["bus"] != args.bus:
            continue
        if p["endpoint"] != args.endpoint:
            continue
        ts = ts_sec + ts_usec / 1_000_000.0
        if t0 is None:
            t0 = ts
        packets.append((ts - t0, p))

    if not packets:
        print("no matching interrupt IN packets found", file=sys.stderr)
        return 1

    if args.summary:
        groups = OrderedDict()
        for rel, p in packets:
            key = p["payload"].hex(" ")
            groups.setdefault(key, []).append(rel)
        print("payload groups (count, first_s, last_s, hex):")
        for hexs, times in groups.items():
            print("%4d  %8.3f  %8.3f  %s" % (len(times), times[0], times[-1], hexs))
        return 0

    print("rel_s      bus dev ep    len  payload")
    for rel, p in packets:
        print("%9.3f  %3d %3d 0x%02X  %3d  %s" % (
            rel, p["bus"], p["device"], p["endpoint"], len(p["payload"]),
            p["payload"].hex(" ")
        ))
    return 0


if __name__ == "__main__":
    sys.exit(main())
