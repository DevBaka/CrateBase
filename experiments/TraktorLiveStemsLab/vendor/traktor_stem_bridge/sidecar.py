"""Traktor Pro 4 native stem sidecar path prediction."""

from __future__ import annotations

import base64
import struct
from dataclasses import dataclass

ALPHABET = "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345"
TRACK_ID_SIZE = 0x100
INITIAL_STATE = (0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476)
SHIFTS = (
    [7, 12, 17, 22] * 4
    + [5, 9, 14, 20] * 4
    + [4, 11, 16, 23] * 4
    + [6, 10, 15, 21] * 4
)
TABLE = (
    0xD76AA478, 0xE8C7B756, 0x242070DB, 0xC1BDCEEE,
    0xF57C0FAF, 0x4787C62A, 0xA8304613, 0xFD469501,
    0x698098D8, 0x8B44F7AF, 0xFFFF5BB1, 0x895CD7BE,
    0x6B901122, 0xFD987193, 0xA679438E, 0x49B40821,
    0xF61E2562, 0xC040B340, 0x265E5A51, 0xE9B6C7AA,
    0xD62F105D, 0x02441453, 0xD8A1E681, 0xE7D3FBC8,
    0x21E1CDE6, 0xC33707D6, 0xF4D50D87, 0x455A14ED,
    0xA9E3E905, 0xFCEFA3F8, 0x676F02D9, 0x8D2A4C8A,
    0xFFFA3942, 0x8771F681, 0x6D9D6122, 0xFDE5380C,
    0xA4BEEA44, 0x4BDECFA9, 0xF6BB4B60, 0xBEBFBC70,
    0x289B7EC6, 0xEAA127FA, 0xD4EF3085, 0x04881D05,
    0xD9D4D039, 0xE6DB99E5, 0x1FA27CF8, 0xC4AC5665,
    0xF4292244, 0x432AFF97, 0xAB9423A7, 0xFC93A039,
    0x655B59C3, 0x8F0CCC92, 0xFFEFF47D, 0x85845DD1,
    0x6FA87E4F, 0xFE2CE6E0, 0xA3014314, 0x4E0811A1,
    0xF7537E82, 0xBD3AF235, 0x2AD7D2BB, 0xEB86D391,
)


@dataclass(frozen=True)
class SidecarPrediction:
    """Predicted native sidecar location."""

    shard: int
    basename: str

    @property
    def filename(self) -> str:
        return f"{self.basename}.stem.mp4"

    @property
    def relative_path(self) -> str:
        return f"Stems/{self.shard:03d}/{self.filename}"


def predict_sidecar(audio_id: str) -> SidecarPrediction:
    """Predict the native sidecar path components for a collection `AUDIO_ID`."""

    track_id = decode_audio_id(audio_id)
    return predict_sidecar_from_track_id(track_id)


def predict_sidecar_from_track_id(track_id: bytes) -> SidecarPrediction:
    """Predict the native sidecar path components for a raw 256-byte TrackID."""

    if len(track_id) != TRACK_ID_SIZE:
        raise ValueError(f"TrackID must be {TRACK_ID_SIZE} bytes, got {len(track_id)}")

    words = _traktor_md5_transform_byte_array(track_id)
    shard = words[0] & 0x7F
    basename = _md5_words_to_string(words)
    return SidecarPrediction(shard=shard, basename=basename)


def decode_audio_id(audio_id: str) -> bytes:
    """Decode Traktor's base64 TrackID, accepting omitted padding."""

    padding = "=" * ((4 - len(audio_id) % 4) % 4)
    return base64.b64decode(audio_id + padding)


def _rotate_left_32(value: int, count: int) -> int:
    return ((value << count) | (value >> (32 - count))) & 0xFFFFFFFF


def _traktor_md5_transform_byte_array(data: bytes) -> tuple[int, int, int, int]:
    """Reproduce Traktor's MD5::transformByteArray helper.

    This is not standard MD5 finalization. It uses standard MD5 compression
    rounds, then processes one final zero-padded 64-byte block without appending
    MD5's normal 0x80 byte or bit-length footer.
    """

    state = list(INITIAL_STATE)
    full_length = len(data) // 64 * 64
    blocks = [data[offset : offset + 64] for offset in range(0, full_length, 64)]
    remainder = data[full_length:]
    blocks.append(remainder + b"\0" * (64 - len(remainder)))

    for block in blocks:
        words = list(struct.unpack("<16I", block))
        a, b, c, d = state
        original = (a, b, c, d)

        for index in range(64):
            if index < 16:
                func = (b & c) | (~b & d)
                word_index = index
            elif index < 32:
                func = (d & b) | (~d & c)
                word_index = (5 * index + 1) % 16
            elif index < 48:
                func = b ^ c ^ d
                word_index = (3 * index + 5) % 16
            else:
                func = c ^ (b | ~d)
                word_index = (7 * index) % 16

            value = (a + func + TABLE[index] + words[word_index]) & 0xFFFFFFFF
            a, d, c, b = d, c, b, (b + _rotate_left_32(value, SHIFTS[index])) & 0xFFFFFFFF

        state = [(current + delta) & 0xFFFFFFFF for current, delta in zip(original, (a, b, c, d))]

    return tuple(state)


def _md5_words_to_string(words: tuple[int, int, int, int]) -> str:
    chars = []
    for word in words:
        for shift in (0, 5, 10, 15, 20, 25, 30):
            chars.append(ALPHABET[(word >> shift) & 0x1F])
    return "".join(chars)

