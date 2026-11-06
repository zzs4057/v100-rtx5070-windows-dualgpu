# Troubleshooting

## Only one GPU appears in nvidia-smi

Check:

```powershell
nvidia-smi -L
Get-PnpDevice -PresentOnly -Class Display |
  Select-Object Status,FriendlyName,Problem,InstanceId
```

If the missing device has Code 10 or Code 31, capture the driver version and
INF before changing anything.

## V100 works but RTX 5070 has Code 31

Typical cause: the 5070 is still bound to an older INF while `nvlddmkm` has
already switched to a different shared kernel version. Restore the 581.15 pair
in the tested order instead of stacking another driver version.

## RTX 5070 works but V100 disappears after update

A Windows Update or NVIDIA App upgrade probably replaced the shared kernel.
Roll back the snapshot or reinstall the retained 581.15 packages without a
clean install.

## llama.cpp reports out-of-memory despite 16 + 8 GB

The GPUs do not form a unified memory pool. Reduce context size, lower KV cache
precision, change tensor split, or choose a smaller quantisation.

Start with:

```text
--split-mode layer
--tensor-split 68,32
-c 4096
```

Adjust the ratio based on the actual free VRAM shown in the GUI and logs.

## Multimodal projector fails to load

Use a projector that matches the main model architecture. Do not assume that
any `mmproj` or MTP file is compatible with every GGUF.

## UI cannot find a model

Place `.gguf` files under `models/`. The launcher reads the directory each time
you press refresh.

## Reporting an issue

Include:

- Windows version
- GPU model and VRAM
- `nvidia-smi` output
- device problem code
- launcher command with local paths removed
- sanitized llama.cpp error lines

Do not attach full logs, model weights, UUIDs, serial numbers or screenshots
showing personal paths.
