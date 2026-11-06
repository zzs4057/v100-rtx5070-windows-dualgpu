# Successful dual-GPU run: sanitized evidence

## Snapshot

Date: `2026-11-05 09:21` (Asia/Shanghai)

| Field | Tesla V100-SXM2-16GB | RTX 5070 Laptop GPU |
|---|---|---|
| Windows health | Healthy | Healthy |
| VRAM | 16384 MiB | 8151 MiB |
| Driver | 581.15 | 581.15 |
| Driver model | WDDM | WDDM |
| INF | oem33.inf | oem32.inf |
| PCI bus | 00000000:04:00.0 | 00000000:01:00.0 |

The monitor sampled both devices as healthy and active at the same time.

## llama.cpp device discovery

The sanitized log excerpt was:

```text
CUDA0: Tesla V100-SXM2-16GB (16383 MiB, 15072 MiB free)
CUDA1: NVIDIA GeForce RTX 5070 Laptop GPU (8150 MiB, 6910 MiB free)
```

## Layer allocation

A 35B-class MoE GGUF with IQ2_M quantisation was loaded with all repeating
layers offloaded:

```text
layers offloaded: 41/41
CUDA0 layers:     0-26
CUDA1 layers:     27-40
CUDA0 buffer:     7163.59 MiB
CUDA1 buffer:     3736.65 MiB
CUDA0 KV cache:   192.00 MiB
CUDA1 KV cache:   128.00 MiB
```

## What was intentionally removed

This public repository excludes:

- the model file and exact model name
- local file-system paths
- full llama.cpp and monitor logs
- GPU UUIDs, serial numbers and Windows device instance IDs
- user, host, network and account information

The original sanitized summary is sufficient to verify that both GPUs were
used by the same llama.cpp process. Raw evidence remains private.

## Reproduction limit

This is a single successful machine configuration, not a benchmark guarantee.
Driver packaging, firmware, eGPU topology and model allocation can change the
result.
