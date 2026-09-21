# Client stall investigation — 2026-09-21

## Current conclusion

The observed full-client freeze was most likely in the Vulkan/Wayland/NVIDIA rendering path, not in socket I/O or packet replay. This is a strong diagnosis for the captured incident, not proof that every future freeze has the same cause. Switching the launcher to OpenGL Compatibility has appeared to resolve it so far; longer testing under the same minimize/workspace-switching conditions is still useful.

## Captured incident

- Client process: PID `3849379`, launched with Godot 4.7.2 Mono, native Wayland, Forward+/Vulkan, and `--gpu-index 1` for the RTX 3050 6GB. The other GPU is an RTX 5090 and should not be used for this client.
- The user reported a complete UI freeze: no clicks, hover effects, or keys worked. The process did not disconnect and later became playable without reconnecting.
- The stall log recorded a start at `2026-09-21T04:23:14Z` and recovery at `04:42:11Z`, for `1141.7s` (19 minutes 1.7 seconds). All START/ONGOING records showed `activity=frame-complete-focused`.
- The inbound packet count rose from 5 to 690 while the main thread was stopped. Managed memory stayed around 82.6–82.7 MB during the stall, then read 70.3 MB on recovery. This was not evidence of a managed-memory spiral.
- While the client was frozen, `/proc/3849379/task/3849379/wchan` showed the main thread in `drm_syncobj_array_wait_timeout.constprop.0`, a kernel DRM synchronization wait. The network receive and timer threads were still running/waiting normally. This is the strongest evidence against a socket timeout as the cause of this incident.
- The recovery line showed `activity=network-send-complete`. That is the most recent application activity when the watchdog reported recovery, not evidence that a socket send caused the preceding stall. The earlier activity records and live main-thread wait are more informative.
- An earlier kernel log after suspend contained NVIDIA GPU 1 power-management assertions, but no matching GPU error was found at the exact stall time. A native stack trace was unavailable because debugger attach was denied, so the precise Godot call site remains unconfirmed.

Godot has reports of [a similar DRM synchronization wait during Vulkan swapchain acquisition](https://github.com/godotengine/godot/issues/117712) and [Wayland/NVIDIA/Vulkan freezes](https://github.com/godotengine/godot/issues/111931). These are context, not proof that this incident is the same engine bug.

## Mitigation and queue behavior

`run.sh` now keeps the Vulkan command commented out and launches native Wayland with `--rendering-method gl_compatibility --rendering-driver opengl3`. Its `DRI_PRIME=1` selects the RTX 3050 for OpenGL; a self-closing Godot probe printed `Compatibility - Using Device: NVIDIA - NVIDIA GeForce RTX 3050`. Godot's `--gpu-index` does not select a GPU for Compatibility/OpenGL, so leaving that flag on the OpenGL command would be misleading.

The `ConcurrentQueue<string>` inbox, background PING response, and bounded main-thread drain should stay for now. They cannot prevent a blocked GPU fence, but they keep incoming packets and keepalives moving while rendering is blocked and limit catch-up work after it resumes. The drain is capped at 64 packets and a cooperative 4 ms budget per frame; a large backlog can therefore take multiple frames to clear.

## If it happens again

1. Preserve the stall log at `~/.local/share/godot/app_userdata/Goose2ClientGodot/client-stalls.log`. It writes SESSION START, STALL STARTED, roughly one STALL ONGOING per minute, and STALL RECOVERED. Note the UTC timestamps and PID.
2. While still frozen, find the current process with `pgrep -af godot-mono`, then inspect the main thread with `cat /proc/<PID>/task/<PID>/wchan` and `ps -o pid,stat,etime,wchan:40,cmd -p <PID>`. A `drm_syncobj...` wait again would strengthen the renderer diagnosis; a different wait needs fresh investigation.
3. Check whether the client was actually launched in OpenGL mode. Godot's `--verbose` startup output names the rendering API and GPU. The expected line is `OpenGL ... Compatibility - Using Device: NVIDIA - NVIDIA GeForce RTX 3050`.
4. Record whether the freeze followed minimizing, switching workspaces, or suspend/resume. If relevant, inspect kernel messages around the UTC stall time with `journalctl -k --since '2026-09-21 04:20:00 UTC' --until '2026-09-21 04:45:00 UTC'`; adjust the dates and times for the new incident and look for NVIDIA `NVRM`/`Xid` or DRM errors.
5. If OpenGL also stalls, keep the new log and main-thread wait result. Do not assume it is the same Vulkan issue: compare the new evidence before changing the queue or network code.
