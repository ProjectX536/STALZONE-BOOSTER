# Фикстуры PresentMon

Все файлы записаны так же, как их пишет PresentMon: UTF-8 с BOM, концы строк CRLF.

## Реальный захват

`pm26-firefox.csv` — PresentMon 2.6.0 (`scripts/Install-PresentMon.ps1`), метрики по умолчанию:

```
PresentMon-2.6.0-x64.exe --process_name firefox.exe --timed 20 --terminate_after_timed --no_console_stats --output_file pm26-firefox.csv
```

Оставлены заголовок и первые 400 из 476 строк данных, колонки не тронуты. Один процесс, одна swap chain.

Сначала предполагался захват `dwm.exe`, но на этой машине (Windows 11, сборка 26300) PresentMon
не увидел у `dwm.exe` ни одного кадра: код выхода 0, CSV не создан. Захват всех процессов показал кадры
только у firefox, поэтому взят он.

`pm26-qpc.csv` — PresentMon 2.6.0, захват всех процессов с метками QPC:

```
PresentMon-2.6.0-x64.exe --timed 4 --terminate_after_timed --no_console_stats --qpc_time --output_file qpc.csv --session_name BoosterProbe --stop_existing_session
```

Оставлены заголовок и первые 400 из 421 строки `RobloxPlayerBeta.exe` (PID 1152, одна swap chain), колонки
не тронуты, строки других процессов убраны. `TimeInQPC` и `CPUStartQPC` — тики QueryPerformanceCounter.
На кадре 248 игра перешла из `Composed: Flip` в `Hardware: Independent Flip` — в файле три режима показа.

## Синтетические файлы

Заголовки скопированы из настоящего вывода PresentMon 2.6.0, строки придуманы. Имя файла помечено `.synthetic`.

| Файл | Заголовок | Что проверяет |
|---|---|---|
| `pm-v1.synthetic.csv` | `--v1_metrics` | колонка `msBetweenPresents` с маленькой буквы; 10 кадров `game.exe` |
| `pm-v2-frametime.synthetic.csv` | `--v2_metrics --track_frame_type` | колонка `FrameTime` и типы кадров `Application` / `Intel XeSS-FG` |
| `pm-header-only.csv` | по умолчанию | ни одной строки данных |
| `pm-two-swapchains.synthetic.csv` | по умолчанию | 300 кадров на swap chain `0xA` и 20 на `0xB` вперемешку |
| `pm-two-pids.synthetic.csv` | по умолчанию | перезапуск игры: PID 100 — 50 кадров, затем PID 200 — 400 кадров |
| `pm-unknown-columns.synthetic.csv` | урезанный | нет ни `MsBetweenPresents`, ни `FrameTime` |
