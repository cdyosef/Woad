import struct, sys

def varlen(data, i):
    value = 0
    while True:
        b = data[i]; i += 1
        value = (value << 7) | (b & 0x7f)
        if not b & 0x80:
            return value, i

def read(path):
    data = open(path, 'rb').read()
    assert data[:4] == b'MThd'
    fmt, ntracks, division = struct.unpack('>HHH', data[8:14])
    i = 8 + struct.unpack('>I', data[4:8])[0]
    tracks = []
    for _ in range(ntracks):
        assert data[i:i+4] == b'MTrk', data[i:i+4]
        length = struct.unpack('>I', data[i+4:i+8])[0]
        j, end = i + 8, i + 8 + length
        tick, status, events, name = 0, None, [], ''
        while j < end:
            delta, j = varlen(data, j); tick += delta
            b = data[j]
            if b == 0xff:
                kind = data[j+1]; size, k = varlen(data, j+2); payload = data[k:k+size]; j = k + size
                if kind == 0x51: events.append((tick, 'tempo', int.from_bytes(payload, 'big')))
                elif kind == 0x58: events.append((tick, 'timesig', payload[0], 2 ** payload[1]))
                elif kind == 0x03: name = payload.decode('latin-1')
                continue
            if b in (0xf0, 0xf7):
                size, k = varlen(data, j+1); j = k + size; continue
            if b & 0x80: status = b; j += 1
            kind = status & 0xf0
            if kind in (0x80, 0x90, 0xa0, 0xb0, 0xe0):
                a, c = data[j], data[j+1]; j += 2
                if kind == 0x90 and c > 0: events.append((tick, 'on', a, c, status & 15))
                elif kind in (0x80, 0x90): events.append((tick, 'off', a, 0, status & 15))
                elif kind == 0xb0: events.append((tick, 'cc', a, c))
            else:
                j += 1
                if kind == 0xc0: events.append((tick, 'program', data[j-1]))
        tracks.append((name, events))
        i = end
    return fmt, division, tracks

def notes(events):
    open_, out = {}, []
    for e in events:
        if e[1] == 'on': open_[e[2]] = (e[0], e[3])
        elif e[1] == 'off' and e[2] in open_:
            start, vel = open_.pop(e[2]); out.append((start, e[0] - start, e[2], vel))
    return sorted(out)

if __name__ == '__main__':
    fmt, division, tracks = read(sys.argv[1])
    print('format', fmt, 'ticks/beat', division)
    for name, events in tracks:
        ns = notes(events)
        meta = [e for e in events if e[1] in ('tempo', 'timesig', 'program')]
        print(f'track "{name}": {len(ns)} notes, meta {meta[:6]}')
        if ns:
            print('  range', min(n[2] for n in ns), max(n[2] for n in ns), 'end tick', max(n[0]+n[1] for n in ns))
            print('  first', ns[:12])
            from collections import Counter
            print('  durations', Counter(n[1] for n in ns).most_common(8))
            print('  start mod beat', Counter(n[0] % division for n in ns).most_common(8))
