using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using AlphaPlay.Models;

namespace AlphaPlay.Services
{
    public sealed class RemoteControlServerService
    {
        private WebApplication? _app;

        public bool IsRunning => _app != null;
        public int CurrentPort { get; private set; }

        public async Task StartAsync(int port)
        {
            if (IsRunning)
            {
                if (CurrentPort == port)
                {
                    return;
                }

                await StopAsync();
            }

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = "AlphaPlay.RemoteControl"
            });

            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            WebApplication app = builder.Build();

            app.MapGet("/", async context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync(GetHomePageHtml(), Encoding.UTF8);
            });

            app.MapGet("/manifest.webmanifest", () => Results.Text(GetManifestJson(), "application/manifest+json", Encoding.UTF8));

            app.MapGet("/sw.js", () => Results.Text(GetServiceWorkerJs(), "application/javascript", Encoding.UTF8));

            app.MapGet("/pwa/icon-192.png", async context => await WritePwaIconAsync(context, "icon-192.png"));

            app.MapGet("/pwa/icon-512.png", async context => await WritePwaIconAsync(context, "icon-512.png"));


            app.MapGet("/api/ping", () => Results.Json(new
            {
                ok = true,
                app = "AlphaPlay",
                remoteControl = "online",
                timestamp = DateTimeOffset.Now
            }));

            app.MapGet("/api/auth/config", () =>
            {
                AppSettings settings = SettingsService.LoadSettings();
                return Results.Json(new
                {
                    requirePin = settings.RemoteControlRequirePin
                });
            });

            app.MapPost("/api/auth/login", async (HttpContext context) =>
            {
                PinLoginRequest? request = await context.Request.ReadFromJsonAsync<PinLoginRequest>();
                AppSettings settings = SettingsService.LoadSettings();

                if (!settings.RemoteControlRequirePin || string.Equals(request?.Pin, settings.RemoteControlPin, StringComparison.Ordinal))
                {
                    return (IResult)Results.Json(new { ok = true, message = "Acesso liberado." });
                }

                return (IResult)Results.Json(new { ok = false, message = "PIN incorreto." }, statusCode: StatusCodes.Status401Unauthorized);
            });

            app.MapGet("/api/status", (HttpContext context) =>
            {
                if (!IsAuthorized(context))
                {
                    return UnauthorizedJson();
                }

                return (IResult)Results.Json(RemoteControlManager.GetStatus());
            });

            app.MapGet("/api/tracks", (HttpContext context) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.GetTracks());
            });

            app.MapGet("/api/queue", (HttpContext context) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.GetQueue());
            });

            app.MapPost("/api/tracks/{trackId:int}/add", (HttpContext context, int trackId) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteLibraryCommand("add", trackId));
            });

            app.MapPost("/api/tracks/{trackId:int}/play", (HttpContext context, int trackId) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteLibraryCommand("play", trackId));
            });

            app.MapPost("/api/queue/clear", (HttpContext context) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteQueueCommand("clear", 0));
            });

            app.MapPost("/api/queue/{index:int}/play", (HttpContext context, int index) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteQueueCommand("play", index));
            });

            app.MapPost("/api/queue/{index:int}/remove", (HttpContext context, int index) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteQueueCommand("remove", index));
            });

            app.MapPost("/api/queue/{index:int}/up", (HttpContext context, int index) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteQueueCommand("up", index));
            });

            app.MapPost("/api/queue/{index:int}/down", (HttpContext context, int index) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteQueueCommand("down", index));
            });

            app.MapPost("/api/queue/{index:int}/move/{newIndex:int}", (HttpContext context, int index, int newIndex) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteQueueCommand("move", index, newIndex));
            });

            app.MapPost("/api/command/set-volume/{volume:int}", (HttpContext context, int volume) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.SetVolume(volume));
            });

            app.MapPost("/api/command/{command}", (HttpContext context, string command) =>
            {
                if (!IsAuthorized(context)) return UnauthorizedJson();
                return (IResult)Results.Json(RemoteControlManager.ExecuteCommand(command));
            });

            try
            {
                await app.StartAsync();
                _app = app;
                CurrentPort = port;
            }
            catch
            {
                await app.DisposeAsync();
                _app = null;
                CurrentPort = 0;
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (_app == null)
            {
                CurrentPort = 0;
                return;
            }

            WebApplication app = _app;
            _app = null;
            CurrentPort = 0;

            await app.StopAsync(TimeSpan.FromSeconds(2));
            await app.DisposeAsync();
        }

        private sealed class PinLoginRequest
        {
            public string? Pin { get; set; }
        }

        private static bool IsAuthorized(HttpContext context)
        {
            AppSettings settings = SettingsService.LoadSettings();

            if (!settings.RemoteControlRequirePin)
            {
                return true;
            }

            string providedPin = context.Request.Headers["X-AlphaPlay-Pin"].ToString();
            return !string.IsNullOrWhiteSpace(providedPin) &&
                   string.Equals(providedPin, settings.RemoteControlPin, StringComparison.Ordinal);
        }

        private static IResult UnauthorizedJson()
        {
            return Results.Json(new
            {
                ok = false,
                authorized = false,
                message = "PIN necessário para acessar o controle remoto."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        private static async Task WritePwaIconAsync(HttpContext context, string fileName)
        {
            string? iconPath = FindPwaIconPath(fileName);
            if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Ícone não encontrado.", Encoding.UTF8);
                return;
            }

            context.Response.ContentType = "image/png";
            context.Response.Headers.CacheControl = "public, max-age=86400";
            await context.Response.SendFileAsync(iconPath);
        }

        private static string? FindPwaIconPath(string fileName)
        {
            string[] basePaths =
            {
                AppContext.BaseDirectory,
                Directory.GetCurrentDirectory(),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."))
            };

            foreach (string basePath in basePaths)
            {
                string candidate = Path.Combine(basePath, "assets", "pwa", fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string GetManifestJson()
        {
            return """
{
  "name": "AlphaPlay Controle Remoto",
  "short_name": "AlphaPlay",
  "description": "Controle remoto local do AlphaPlay pelo celular.",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "orientation": "portrait",
  "background_color": "#071020",
  "theme_color": "#071020",
  "icons": [
    {
      "src": "/pwa/icon-192.png",
      "sizes": "192x192",
      "type": "image/png",
      "purpose": "any maskable"
    },
    {
      "src": "/pwa/icon-512.png",
      "sizes": "512x512",
      "type": "image/png",
      "purpose": "any maskable"
    }
  ]
}
""";
        }

        private static string GetServiceWorkerJs()
        {
            return """
const CACHE_NAME = 'alphaplay-controle-v1';
const CORE_ASSETS = ['/', '/manifest.webmanifest', '/pwa/icon-192.png', '/pwa/icon-512.png'];

self.addEventListener('install', event => {
  event.waitUntil(caches.open(CACHE_NAME).then(cache => cache.addAll(CORE_ASSETS)).catch(() => undefined));
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== CACHE_NAME).map(key => caches.delete(key)))));
  self.clients.claim();
});

self.addEventListener('fetch', event => {
  const url = new URL(event.request.url);
  if (url.pathname.startsWith('/api/')) return;
  event.respondWith(fetch(event.request).catch(() => caches.match(event.request).then(response => response || caches.match('/'))));
});
""";
        }

        private static string GetHomePageHtml()
        {
            return """
<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <meta name="theme-color" content="#071020">
  <title>AlphaPlay Controle Remoto</title>
  <link rel="manifest" href="/manifest.webmanifest">
  <link rel="apple-touch-icon" href="/pwa/icon-192.png">
  <meta name="mobile-web-app-capable" content="yes">
  <meta name="apple-mobile-web-app-capable" content="yes">
  <meta name="apple-mobile-web-app-title" content="AlphaPlay">
  <meta name="apple-mobile-web-app-status-bar-style" content="black-translucent">
  <style>
    :root {
      color-scheme: dark;
      --bg1: #16477e;
      --bg2: #071020;
      --bg3: #03050d;
      --panel: rgba(12, 20, 38, .94);
      --panel2: rgba(7, 16, 32, .94);
      --line: #26365f;
      --line2: rgba(255,255,255,.08);
      --text: #ffffff;
      --muted: #a9b9d8;
      --muted2: #7f92b8;
      --accent: #00d9ff;
      --accent2: #2f7bff;
      --green: #22c55e;
      --green-soft: rgba(34, 197, 94, .16);
      --red: #ef4444;
      --red-soft: rgba(239, 68, 68, .14);
      --yellow: #facc15;
    }

    * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }

    html { min-height: 100%; background: var(--bg3); }

    body {
      margin: 0;
      min-height: 100vh;
      font-family: Arial, Helvetica, sans-serif;
      background:
        radial-gradient(circle at 18% 0%, rgba(0, 217, 255, .22), transparent 34%),
        radial-gradient(circle at 100% 0%, rgba(47, 123, 255, .22), transparent 36%),
        linear-gradient(145deg, var(--bg1), var(--bg2) 54%, var(--bg3));
      color: var(--text);
      padding: max(14px, env(safe-area-inset-top)) 14px max(18px, env(safe-area-inset-bottom));
    }

    .app { width: 100%; max-width: 620px; margin: 0 auto; }

    .top {
      position: sticky;
      top: 0;
      z-index: 10;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 12px;
      padding: 9px 0 13px;
      backdrop-filter: blur(10px);
    }

    .brand h1 { font-size: clamp(22px, 6vw, 30px); margin: 0; letter-spacing: -.5px; }
    .brand p { color: var(--muted); margin: 4px 0 0; font-size: 13px; }

    .badge {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      white-space: nowrap;
      background: var(--green-soft);
      color: #86efac;
      border: 1px solid rgba(34, 197, 94, .35);
      border-radius: 999px;
      padding: 8px 12px;
      font-size: 12px;
      font-weight: 900;
      box-shadow: 0 10px 24px rgba(0,0,0,.18);
    }
    .badge.offline { background: var(--red-soft); color: #fca5a5; border-color: rgba(239,68,68,.36); }
    .dot { width: 8px; height: 8px; border-radius: 50%; background: var(--green); box-shadow: 0 0 0 4px rgba(34,197,94,.12); }
    .badge.offline .dot { background: var(--red); box-shadow: 0 0 0 4px rgba(239,68,68,.12); }

    .card {
      background: linear-gradient(180deg, rgba(12, 20, 38, .98), rgba(7, 16, 32, .93));
      border: 1px solid var(--line);
      border-radius: 26px;
      padding: 18px;
      box-shadow: 0 20px 60px rgba(0,0,0,.35);
      margin-bottom: 14px;
      overflow: hidden;
    }

    .section-title { display: flex; align-items: center; justify-content: space-between; gap: 10px; color: var(--muted); font-size: 13px; margin-bottom: 10px; }
    .state-pill { border: 1px solid var(--line2); border-radius: 999px; padding: 5px 9px; color: white; background: rgba(255,255,255,.05); font-size: 12px; font-weight: 800; }
    .now-title { font-size: clamp(20px, 5vw, 28px); line-height: 1.18; font-weight: 900; margin-bottom: 8px; word-break: break-word; }
    .now-subtitle { color: var(--muted); font-size: 14px; margin-bottom: 16px; line-height: 1.42; }

    .mini-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; margin-top: 14px; }
    .mini-card { background: rgba(255,255,255,.045); border: 1px solid var(--line2); border-radius: 16px; padding: 12px; }
    .mini-label { color: var(--muted2); font-size: 12px; margin-bottom: 4px; }
    .mini-value { font-weight: 900; font-size: 18px; }

    .progress { width: 100%; height: 10px; appearance: none; border-radius: 999px; background: #1b2a4d; outline: none; }
    .progress::-webkit-slider-thumb { appearance: none; width: 22px; height: 22px; border-radius: 50%; background: var(--accent); box-shadow: 0 0 0 6px rgba(0,217,255,.15); }
    .progress::-moz-range-thumb { width: 22px; height: 22px; border: 0; border-radius: 50%; background: var(--accent); box-shadow: 0 0 0 6px rgba(0,217,255,.15); }
    .time-row { display: flex; justify-content: space-between; color: var(--muted2); font-size: 12px; margin-top: 9px; font-weight: 700; }

    .controls-main { display: grid; grid-template-columns: 1fr 1.35fr 1fr; gap: 12px; margin-top: 18px; align-items: center; }
    .controls-secondary { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 12px; }

    button {
      border: 1px solid var(--line);
      background: linear-gradient(180deg, #132748, #0d1b33);
      color: white;
      border-radius: 17px;
      min-height: 52px;
      font-size: 14px;
      font-weight: 900;
      cursor: pointer;
      padding: 0 12px;
      box-shadow: 0 10px 24px rgba(0,0,0,.18);
    }
    button:active { transform: scale(.98); }
    button.small { min-height: 42px; border-radius: 13px; font-size: 12px; padding: 0 10px; box-shadow: none; }
    button.danger { border-color: rgba(239, 68, 68, .45); background: rgba(127, 29, 29, .52); }
    .primary { min-height: 72px; background: linear-gradient(135deg, #1164d8, #00a9d6); border-color: rgba(0,217,255,.65); font-size: 18px; }

    .volume-row { display: grid; grid-template-columns: 54px 1fr 54px; gap: 10px; align-items: center; }
    .volume-value { color: white; font-weight: 900; text-align: center; margin-top: 8px; }

    .tabs { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; margin-bottom: 12px; background: rgba(255,255,255,.045); border: 1px solid var(--line2); border-radius: 18px; padding: 6px; }
    .tabs button { min-height: 46px; box-shadow: none; background: transparent; border-color: transparent; }
    .tabs button.active { background: linear-gradient(135deg, #164a9d, #0b7895); border-color: rgba(0,217,255,.55); }
    .tools { display: grid; grid-template-columns: 1fr auto; gap: 10px; margin-bottom: 12px; }
    .search { width: 100%; border: 1px solid var(--line); background: #071020; color: white; border-radius: 15px; min-height: 48px; padding: 0 14px; font-size: 15px; outline: none; }
    .list { display: grid; gap: 10px; }
    .item { background: rgba(7, 16, 32, .96); border: 1px solid var(--line); border-radius: 18px; padding: 13px; }
    .item.current { border-color: rgba(0,217,255,.75); box-shadow: 0 0 0 1px rgba(0,217,255,.2) inset; }
    .item-title { font-weight: 900; line-height: 1.25; margin-bottom: 5px; word-break: break-word; }
    .item-meta { color: var(--muted2); font-size: 12px; margin-bottom: 10px; line-height: 1.35; }
    .item-actions { display: grid; grid-template-columns: repeat(2, 1fr); gap: 8px; }
    .queue-actions { display: grid; grid-template-columns: 1.2fr .75fr .75fr 1.15fr; gap: 8px; }
    .empty-box { background: rgba(7, 16, 32, .75); border: 1px dashed var(--line); border-radius: 18px; padding: 16px; color: var(--muted); font-size: 14px; line-height: 1.45; }

    .login-screen { position: fixed; inset: 0; z-index: 20; background: radial-gradient(circle at top, #12345f, #070b1a 58%, #03050d); display: none; align-items: center; justify-content: center; padding: 18px; }
    .login-card { width: 100%; max-width: 430px; background: var(--panel); border: 1px solid var(--line); border-radius: 24px; padding: 22px; box-shadow: 0 20px 60px rgba(0,0,0,.45); }
    .login-card h2 { margin: 0 0 8px; font-size: 24px; }
    .login-card p { margin: 0 0 18px; color: var(--muted); line-height: 1.45; }
    .pin-input { width: 100%; min-height: 56px; border: 1px solid var(--line); background: #071020; color: white; border-radius: 17px; padding: 0 16px; font-size: 23px; font-weight: 900; text-align: center; letter-spacing: 8px; outline: none; }
    .pin-error { min-height: 20px; color: #fca5a5; font-size: 13px; margin: 10px 0 0; text-align: center; }
    .login-card button { width: 100%; margin-top: 14px; }

    .toast { position: fixed; left: 14px; right: 14px; bottom: max(16px, env(safe-area-inset-bottom)); max-width: 620px; margin: 0 auto; background: #101a30; border: 1px solid var(--line); border-radius: 17px; padding: 13px 15px; color: white; box-shadow: 0 16px 45px rgba(0,0,0,.35); opacity: 0; transform: translateY(18px); pointer-events: none; transition: .2s ease; z-index: 30; }
    .toast.show { opacity: 1; transform: translateY(0); }

    .install-card { display: none; border-color: rgba(0,217,255,.38); background: linear-gradient(180deg, rgba(14, 41, 75, .98), rgba(7, 16, 32, .96)); }
    .install-card.show { display: block; }
    .install-title { font-size: 17px; font-weight: 900; margin-bottom: 7px; }
    .install-text { color: var(--muted); font-size: 13px; line-height: 1.45; margin-bottom: 12px; }
    .install-actions { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }

    @media (max-width: 390px) {
      body { padding-left: 10px; padding-right: 10px; }
      .card { padding: 15px; border-radius: 22px; }
      .controls-main { gap: 8px; }
      .controls-secondary { gap: 8px; }
      button { min-height: 50px; font-size: 13px; padding: 0 8px; }
      .primary { min-height: 68px; font-size: 16px; }
      .queue-actions { grid-template-columns: repeat(2, 1fr); }
    }
  </style>
</head>
<body>
  <div id="loginScreen" class="login-screen">
    <div class="login-card">
      <h2>PIN do AlphaPlay</h2>
      <p>Digite o PIN mostrado no computador para liberar o controle remoto neste celular.</p>
      <input id="pinInput" class="pin-input" type="password" inputmode="numeric" maxlength="4" placeholder="••••">
      <div id="pinError" class="pin-error"></div>
      <button class="primary" onclick="loginWithPin()">Entrar</button>
    </div>
  </div>

  <main class="app" id="mainApp">
    <header class="top">
      <div class="brand">
        <h1>AlphaPlay Controle</h1>
        <p>Controle remoto local</p>
      </div>
      <div id="statusBadge" class="badge"><span class="dot"></span><span id="serverStatus">Online</span></div>
    </header>

    <section id="installCard" class="card install-card">
      <div class="install-title">Instalar controle no celular</div>
      <div id="installText" class="install-text">Adicione o AlphaPlay Controle à tela inicial para abrir como aplicativo.</div>
      <div class="install-actions">
        <button id="installButton" onclick="installApp()">Instalar</button>
        <button onclick="showInstallHelp()">Como instalar</button>
      </div>
    </section>

    <section class="card">
      <div class="section-title"><span>Tocando agora</span><span id="playState" class="state-pill">Parado</span></div>
      <div id="currentTitle" class="now-title">Nenhuma música tocando</div>
      <div id="currentInfo" class="now-subtitle">Aguardando conexão com o player do AlphaPlay.</div>
      <input id="progress" class="progress" type="range" min="0" max="100" value="0" aria-label="Progresso da música">
      <div class="time-row"><span id="currentTime">00:00</span><span id="durationTime">00:00</span></div>
      <div class="controls-main">
        <button onclick="sendCommand('previous')">Anterior</button>
        <button id="playPauseButton" class="primary" onclick="sendCommand('play-pause')">Tocar</button>
        <button onclick="sendCommand('next')">Próxima</button>
      </div>
      <div class="controls-secondary">
        <button onclick="sendCommand('seek-backward')">Voltar 10s</button>
        <button onclick="sendCommand('seek-forward')">Avançar 10s</button>
      </div>
      <div class="mini-grid">
        <div class="mini-card"><div class="mini-label">Biblioteca</div><div id="libraryMini" class="mini-value">0</div></div>
        <div class="mini-card"><div class="mini-label">Fila</div><div id="queueMini" class="mini-value">0</div></div>
      </div>
    </section>

    <section class="card">
      <div class="section-title"><span>Volume</span><span id="volumeLabel" class="state-pill">50%</span></div>
      <div class="volume-row">
        <button onclick="sendCommand('volume-down')">-</button>
        <input id="volume" class="progress" type="range" min="0" max="100" value="50" aria-label="Volume">
        <button onclick="sendCommand('volume-up')">+</button>
      </div>
      <div class="volume-value" id="volumeValue">50%</div>
    </section>

    <section class="card">
      <div class="section-title"><span>Biblioteca e fila</span><span id="counts">0 músicas • 0 na fila</span></div>
      <div class="tabs">
        <button id="tabTracks" class="active" onclick="setTab('tracks')">Músicas</button>
        <button id="tabQueue" onclick="setTab('queue')">Fila</button>
      </div>
      <div class="tools">
        <input id="search" class="search" placeholder="Buscar música..." oninput="renderCurrentTab()">
        <button class="small" onclick="loadLists()">Atualizar</button>
      </div>
      <div id="list" class="list"></div>
    </section>
  </main>

  <div id="toast" class="toast"></div>

  <script>
    const toast = document.getElementById('toast');
    const loginScreen = document.getElementById('loginScreen');
    const pinInput = document.getElementById('pinInput');
    const pinError = document.getElementById('pinError');
    const statusBadge = document.getElementById('statusBadge');
    let tracks = [];
    let queue = [];
    let currentTab = 'tracks';
    let requirePin = false;
    let remotePin = window.localStorage.getItem('alphaplay_remote_pin') || '';
    let deferredInstallPrompt = null;

    function formatTime(seconds) {
      seconds = Number(seconds || 0);
      const min = Math.floor(seconds / 60).toString().padStart(2, '0');
      const sec = Math.floor(seconds % 60).toString().padStart(2, '0');
      return `${min}:${sec}`;
    }

    function escapeHtml(value) {
      return String(value || '').replace(/[&<>\"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '\"': '&quot;' }[char]));
    }

    function setOnlineState(isOnline) {
      document.getElementById('serverStatus').textContent = isOnline ? 'Online' : 'Offline';
      statusBadge.classList.toggle('offline', !isOnline);
    }

    function showToast(message) {
      toast.textContent = message;
      toast.classList.add('show');
      window.clearTimeout(window.__toastTimer);
      window.__toastTimer = window.setTimeout(() => toast.classList.remove('show'), 2400);
    }

    function showLogin(message) {
      loginScreen.style.display = 'flex';
      pinError.textContent = message || '';
      setTimeout(() => pinInput.focus(), 100);
    }

    function hideLogin() {
      loginScreen.style.display = 'none';
      pinError.textContent = '';
      pinInput.value = '';
    }

    async function apiFetch(url, options = {}) {
      const headers = new Headers(options.headers || {});
      if (requirePin && remotePin) headers.set('X-AlphaPlay-Pin', remotePin);
      const response = await fetch(url, { ...options, headers });
      if (response.status === 401) {
        window.localStorage.removeItem('alphaplay_remote_pin');
        remotePin = '';
        showLogin('Digite o PIN para acessar.');
        throw new Error('PIN necessário.');
      }
      return response;
    }

    async function loadAuthConfig() {
      try {
        const response = await fetch('/api/auth/config', { cache: 'no-store' });
        const config = await response.json();
        requirePin = !!config.requirePin;
        if (requirePin && !remotePin) {
          showLogin('PIN obrigatório.');
          return false;
        }
        hideLogin();
        return true;
      } catch {
        setOnlineState(false);
        return false;
      }
    }

    async function loginWithPin() {
      const pin = pinInput.value.trim();
      if (!/^\d{4}$/.test(pin)) {
        pinError.textContent = 'Digite um PIN com 4 números.';
        return;
      }
      try {
        const response = await fetch('/api/auth/login', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ pin })
        });
        const result = await response.json();
        if (!response.ok || !result.ok) {
          pinError.textContent = result.message || 'PIN incorreto.';
          return;
        }
        remotePin = pin;
        window.localStorage.setItem('alphaplay_remote_pin', pin);
        hideLogin();
        await refreshAll();
      } catch {
        pinError.textContent = 'Não foi possível validar o PIN.';
      }
    }

    pinInput.addEventListener('input', () => {
      pinInput.value = pinInput.value.replace(/\D/g, '').slice(0, 4);
      pinError.textContent = '';
    });

    pinInput.addEventListener('keydown', event => {
      if (event.key === 'Enter') loginWithPin();
    });

    async function refreshStatus() {
      try {
        const response = await apiFetch('/api/status', { cache: 'no-store' });
        const status = await response.json();
        setOnlineState(status.serverOnline !== false);
        document.getElementById('currentTitle').textContent = status.currentTitle || 'Nenhuma música tocando';
        document.getElementById('playState').textContent = status.playbackState || (status.isPlaying ? 'Tocando' : 'Parado');
        document.getElementById('playPauseButton').textContent = status.isPlaying ? 'Pausar' : 'Tocar';
        document.getElementById('currentInfo').textContent = status.message || 'Aguardando comandos.';
        document.getElementById('currentTime').textContent = formatTime(status.positionSeconds);
        document.getElementById('durationTime').textContent = formatTime(status.durationSeconds);
        const duration = Number(status.durationSeconds || 0);
        const position = Number(status.positionSeconds || 0);
        document.getElementById('progress').value = duration > 0 ? Math.min(100, Math.round((position / duration) * 100)) : 0;
        const volume = Number(status.volume ?? 50);
        document.getElementById('volume').value = volume;
        document.getElementById('volumeLabel').textContent = `${volume}%`;
        document.getElementById('volumeValue').textContent = `${volume}%`;
        const libraryCount = status.libraryCount || 0;
        const queueCount = status.queueCount || 0;
        document.getElementById('counts').textContent = `${libraryCount} músicas • ${queueCount} na fila`;
        document.getElementById('libraryMini').textContent = libraryCount;
        document.getElementById('queueMini').textContent = queueCount;
      } catch {
        setOnlineState(false);
      }
    }

    async function sendCommand(command) {
      try {
        const response = await apiFetch(`/api/command/${command}`, { method: 'POST' });
        const result = await response.json();
        showToast(result.message || `Comando enviado: ${command}`);
        await refreshAll();
      } catch {
        showToast('Não foi possível enviar o comando.');
      }
    }

    async function post(url) {
      try {
        const response = await apiFetch(url, { method: 'POST' });
        const result = await response.json();
        showToast(result.message || 'Comando executado.');
        await refreshAll();
      } catch {
        showToast('Não foi possível executar a ação.');
      }
    }

    async function loadLists() {
      try {
        const [tracksResponse, queueResponse] = await Promise.all([
          apiFetch('/api/tracks', { cache: 'no-store' }),
          apiFetch('/api/queue', { cache: 'no-store' })
        ]);
        tracks = await tracksResponse.json();
        queue = await queueResponse.json();
        renderCurrentTab();
      } catch { }
    }

    function setTab(tab) {
      currentTab = tab;
      document.getElementById('tabTracks').classList.toggle('active', tab === 'tracks');
      document.getElementById('tabQueue').classList.toggle('active', tab === 'queue');
      document.getElementById('search').placeholder = tab === 'tracks' ? 'Buscar música...' : 'Buscar na fila...';
      renderCurrentTab();
    }

    function renderCurrentTab() {
      if (currentTab === 'tracks') renderTracks(); else renderQueue();
    }

    function normalizeSearchText(value) {
      return (value || '')
        .trim()
        .toLowerCase()
        .normalize('NFD')
        .replace(/[\u0300-\u036f]/g, '');
    }

    function itemStartsWithSearch(item, search) {
      if (!search) return true;
      const title = normalizeSearchText(item.title);
      const fileName = normalizeSearchText(item.fileName);
      const fileNameWithoutExtension = fileName.replace(/\.[^/.]+$/, '');
      return title.startsWith(search) || fileName.startsWith(search) || fileNameWithoutExtension.startsWith(search);
    }

    function renderTracks() {
      const list = document.getElementById('list');
      const search = normalizeSearchText(document.getElementById('search').value);
      const filtered = tracks.filter(item => itemStartsWithSearch(item, search));
      if (filtered.length === 0) {
        list.innerHTML = '<div class="empty-box">Nenhuma música encontrada. Atualize a biblioteca no AlphaPlay do computador ou confira a pasta selecionada.</div>';
        return;
      }
      list.innerHTML = filtered.map(item => `
        <div class="item">
          <div class="item-title">${escapeHtml(item.title)}</div>
          <div class="item-meta">${escapeHtml(item.fileName)}${item.isVideo ? ' • vídeo' : ''}${item.isInQueue ? ' • já está na fila' : ''}</div>
          <div class="item-actions">
            <button class="small" onclick="post('/api/tracks/${item.id}/play')">Tocar agora</button>
            <button class="small" onclick="post('/api/tracks/${item.id}/add')">Adicionar</button>
          </div>
        </div>
      `).join('');
    }

    function renderQueue() {
      const list = document.getElementById('list');
      const search = normalizeSearchText(document.getElementById('search').value);
      const filtered = queue.filter(item => itemStartsWithSearch(item, search));
      if (queue.length === 0) {
        list.innerHTML = '<div class="empty-box">A fila está vazia. Entre em Músicas e adicione itens à fila.</div>';
        return;
      }
      if (filtered.length === 0) {
        list.innerHTML = '<div class="empty-box">Nenhum item encontrado na fila.</div>';
        return;
      }
      list.innerHTML = `
        <button class="danger" onclick="post('/api/queue/clear')">Limpar fila</button>
        ${filtered.map(item => `
          <div class="item ${item.isCurrent ? 'current' : ''}">
            <div class="item-title">${String(item.position).padStart(2, '0')}. ${escapeHtml(item.title)}</div>
            <div class="item-meta">${escapeHtml(item.fileName)}${item.isCurrent ? (item.isPaused ? ' • pausada agora' : ' • tocando agora') : ''}</div>
            <div class="queue-actions">
              <button class="small" onclick="post('/api/queue/${item.index}/play')">Tocar</button>
              <button class="small" onclick="post('/api/queue/${item.index}/up')">↑</button>
              <button class="small" onclick="post('/api/queue/${item.index}/down')">↓</button>
              <button class="small danger" onclick="post('/api/queue/${item.index}/remove')">Remover</button>
            </div>
          </div>
        `).join('')}
      `;
    }

    async function refreshAll() {
      const ready = await loadAuthConfig();
      if (!ready) return;
      await Promise.all([refreshStatus(), loadLists()]);
    }

    document.getElementById('volume').addEventListener('input', event => {
      const value = event.target.value;
      document.getElementById('volumeLabel').textContent = `${value}%`;
      document.getElementById('volumeValue').textContent = `${value}%`;
    });

    document.getElementById('volume').addEventListener('change', event => {
      sendCommand(`set-volume/${event.target.value}`);
    });

    window.addEventListener('beforeinstallprompt', event => {
      event.preventDefault();
      deferredInstallPrompt = event;
      document.getElementById('installCard').classList.add('show');
      document.getElementById('installText').textContent = 'O navegador permite instalar o AlphaPlay Controle neste celular.';
    });

    window.addEventListener('appinstalled', () => {
      deferredInstallPrompt = null;
      document.getElementById('installCard').classList.remove('show');
      showToast('AlphaPlay Controle instalado.');
    });

    async function installApp() {
      if (deferredInstallPrompt) {
        deferredInstallPrompt.prompt();
        await deferredInstallPrompt.userChoice;
        deferredInstallPrompt = null;
        return;
      }
      showInstallHelp();
    }

    function showInstallHelp() {
      const ua = navigator.userAgent || '';
      const isIos = /iPad|iPhone|iPod/.test(ua);
      if (isIos) {
        showToast('No iPhone: toque em Compartilhar e depois em Adicionar à Tela de Início.');
      } else {
        showToast('No Android: abra o menu do navegador e toque em Adicionar à tela inicial ou Instalar app.');
      }
      document.getElementById('installCard').classList.add('show');
    }

    if ('serviceWorker' in navigator) {
      window.addEventListener('load', () => {
        navigator.serviceWorker.register('/sw.js').catch(() => { });
      });
    }

    refreshAll();
    setInterval(refreshStatus, 2500);
    setInterval(loadLists, 5000);
  </script>
</body>
</html>
""";
        }
    }
}
