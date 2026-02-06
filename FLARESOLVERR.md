# FlareSolverr Installation & Configuration Guide

🔗 **Leia em Português abaixo | Read in English first**

---

## Why is FlareSolverr Required?

Crunchyroll is protected by **Cloudflare**, which blocks all direct API requests from servers (HTTP 403 Forbidden). This plugin needs a real browser to bypass Cloudflare's protections.

**FlareSolverr** runs a headless Chrome browser inside a Docker container. The plugin uses **Chrome DevTools Protocol (CDP)** to execute JavaScript `fetch()` calls directly inside that browser — effectively making requests as if a real user were browsing Crunchyroll.

### How it works

```
Plugin → FlareSolverr (keeps Chrome alive) → CDP → Chrome executes fetch() → Crunchyroll API
```

1. The plugin creates a FlareSolverr session to keep Chrome running
2. Authenticates anonymously via CDP (no Crunchyroll account needed)
3. Uses Chrome's browser context to fetch seasons, episodes, and images
4. Auth tokens are cached for 50 minutes to minimize overhead

---

## Installation

### Option 1: Docker Run (Simplest)

```bash
docker run -d \
  --name flaresolverr \
  -p 8191:8191 \
  -e LOG_LEVEL=info \
  --restart unless-stopped \
  ghcr.io/flaresolverr/flaresolverr:latest
```

### Option 2: Docker Compose

Add this to your `docker-compose.yml`:

```yaml
services:
  flaresolverr:
    image: ghcr.io/flaresolverr/flaresolverr:latest
    container_name: flaresolverr
    ports:
      - "8191:8191"
    environment:
      - LOG_LEVEL=info
      - TZ=America/Sao_Paulo # Change to your timezone
    restart: unless-stopped
```

Then run:

```bash
docker compose up -d flaresolverr
```

### Option 3: Alongside Jellyfin in Docker Compose

If you already run Jellyfin in Docker Compose, add FlareSolverr to the same file:

```yaml
services:
  jellyfin:
    image: jellyfin/jellyfin:latest
    container_name: jellyfin
    volumes:
      - /path/to/config:/config
      - /path/to/media:/media
      - /var/run/docker.sock:/var/run/docker.sock # Required for CDP
    group_add:
      - "docker" # Jellyfin needs Docker access for CDP
    ports:
      - "8096:8096"
    restart: unless-stopped

  flaresolverr:
    image: ghcr.io/flaresolverr/flaresolverr:latest
    container_name: flaresolverr
    ports:
      - "8191:8191"
    environment:
      - LOG_LEVEL=info
    restart: unless-stopped
```

> **Important**: Jellyfin needs access to the Docker socket (`/var/run/docker.sock`) so the plugin can communicate with FlareSolverr's Chrome via CDP.

---

## Verify FlareSolverr is Running

```bash
curl -X POST http://localhost:8191/v1 \
  -H "Content-Type: application/json" \
  -d '{"cmd": "sessions.list"}'
```

Expected response:

```json
{
  "status": "ok",
  "message": "...",
  "sessions": []
}
```

---

## Plugin Configuration

### 1. Open Plugin Settings

Go to: `Dashboard > Plugins > Crunchyroll Metadata`

### 2. Configure FlareSolverr

| Setting                   | Value                   | Description                                                                                               |
| ------------------------- | ----------------------- | --------------------------------------------------------------------------------------------------------- |
| **FlareSolverr URL**      | `http://localhost:8191` | URL where FlareSolverr is running. Use `http://flaresolverr:8191` if both are in the same Docker network. |
| **Docker Container Name** | `flaresolverr`          | The `--name` you gave the container. Must match exactly.                                                  |
| **Chrome CDP URL**        | _(leave empty)_         | Advanced: Only set this if you want to override auto-detection of Chrome's DevTools port.                 |

### 3. Save and Restart Jellyfin

```bash
# Linux (systemd)
sudo systemctl restart jellyfin

# Docker
docker restart jellyfin
```

---

## Docker Socket Permissions

The plugin uses `docker exec` to run commands inside the FlareSolverr container (for CDP communication). This requires the Jellyfin process to have access to the Docker socket.

### Linux (systemd install)

```bash
# Add jellyfin user to docker group
sudo usermod -aG docker jellyfin

# Restart Jellyfin to apply
sudo systemctl restart jellyfin
```

### Docker install

Mount the Docker socket into the Jellyfin container:

```yaml
volumes:
  - /var/run/docker.sock:/var/run/docker.sock
```

And add the Docker group:

```yaml
group_add:
  - "docker" # Or the numeric GID of the docker group
```

> To find the docker group GID: `getent group docker | cut -d: -f3`

---

## Troubleshooting

### "FlareSolverr is not reachable"

- Verify FlareSolverr is running: `docker ps | grep flaresolverr`
- Check the URL is correct (default: `http://localhost:8191`)
- If using Docker networks, use the container name: `http://flaresolverr:8191`

### "Permission denied" / Cannot access Docker socket

- Ensure the Jellyfin user is in the `docker` group
- Restart Jellyfin after adding to the group
- Check socket permissions: `ls -la /var/run/docker.sock`

### "No Chrome port found" / CDP connection failed

- FlareSolverr may have restarted — the plugin will auto-recover by creating a new session
- Check FlareSolverr logs: `docker logs flaresolverr`
- Ensure FlareSolverr version is **v3.x** (v3.3.21 or v3.4.6 recommended)

### Metadata still not loading

1. Check Jellyfin logs for `[Crunchyroll]` entries
2. Look for `CDP Auth` messages — they confirm the Cloudflare bypass is working
3. Ensure the anime exists on Crunchyroll in your configured language
4. Try manual identification: Series > Edit Metadata > Identify

### FlareSolverr high memory usage

FlareSolverr runs a full Chrome browser. Expect ~200-400 MB of RAM usage. The plugin manages sessions efficiently and cleans up when done.

---

## Network Diagram

```
┌─────────────────┐     HTTP      ┌──────────────────┐
│                  │──────────────▶│                   │
│    Jellyfin      │   :8191      │   FlareSolverr    │
│  (Plugin)        │              │  (Chrome inside)  │
│                  │◀─────────────│                   │
└────────┬─────────┘   Response   └──────────────────┘
         │                                 │
         │  docker exec (CDP)              │  Chrome fetches
         │  via Docker socket              │  via CDP
         ▼                                 ▼
   ┌───────────┐                   ┌──────────────┐
   │  Docker   │                   │  Crunchyroll │
   │  Socket   │                   │     API      │
   └───────────┘                   └──────────────┘
```

---

## Recommended FlareSolverr Versions

| Version | Status                           |
| ------- | -------------------------------- |
| v3.4.6  | ✅ Tested and working            |
| v3.3.21 | ✅ Tested and working            |
| v2.x    | ❌ Not supported (different API) |

---

---

# 🇧🇷 Guia de Instalação e Configuração do FlareSolverr

## Por que o FlareSolverr é Obrigatório?

A Crunchyroll é protegida pelo **Cloudflare**, que bloqueia todas as requisições diretas à API vindas de servidores (HTTP 403 Forbidden). Este plugin precisa de um navegador real para contornar as proteções do Cloudflare.

O **FlareSolverr** executa um navegador Chrome headless dentro de um container Docker. O plugin usa o **Chrome DevTools Protocol (CDP)** para executar chamadas JavaScript `fetch()` diretamente dentro desse navegador — efetivamente fazendo requisições como se um usuário real estivesse navegando na Crunchyroll.

### Como funciona

```
Plugin → FlareSolverr (mantém Chrome ativo) → CDP → Chrome executa fetch() → API da Crunchyroll
```

1. O plugin cria uma sessão no FlareSolverr para manter o Chrome em execução
2. Autentica anonimamente via CDP (não precisa de conta da Crunchyroll)
3. Usa o contexto do navegador Chrome para buscar temporadas, episódios e imagens
4. Tokens de autenticação são armazenados em cache por 50 minutos

---

## Instalação

### Opção 1: Docker Run (Mais Simples)

```bash
docker run -d \
  --name flaresolverr \
  -p 8191:8191 \
  -e LOG_LEVEL=info \
  --restart unless-stopped \
  ghcr.io/flaresolverr/flaresolverr:latest
```

### Opção 2: Docker Compose

Adicione ao seu `docker-compose.yml`:

```yaml
services:
  flaresolverr:
    image: ghcr.io/flaresolverr/flaresolverr:latest
    container_name: flaresolverr
    ports:
      - "8191:8191"
    environment:
      - LOG_LEVEL=info
      - TZ=America/Sao_Paulo
    restart: unless-stopped
```

Depois execute:

```bash
docker compose up -d flaresolverr
```

### Opção 3: Junto com Jellyfin no Docker Compose

```yaml
services:
  jellyfin:
    image: jellyfin/jellyfin:latest
    container_name: jellyfin
    volumes:
      - /caminho/para/config:/config
      - /caminho/para/media:/media
      - /var/run/docker.sock:/var/run/docker.sock # Necessário para CDP
    group_add:
      - "docker"
    ports:
      - "8096:8096"
    restart: unless-stopped

  flaresolverr:
    image: ghcr.io/flaresolverr/flaresolverr:latest
    container_name: flaresolverr
    ports:
      - "8191:8191"
    environment:
      - LOG_LEVEL=info
    restart: unless-stopped
```

> **Importante**: O Jellyfin precisa de acesso ao Docker socket (`/var/run/docker.sock`) para que o plugin se comunique com o Chrome do FlareSolverr via CDP.

---

## Verificar se o FlareSolverr está Funcionando

```bash
curl -X POST http://localhost:8191/v1 \
  -H "Content-Type: application/json" \
  -d '{"cmd": "sessions.list"}'
```

---

## Configuração do Plugin

Acesse: `Dashboard > Plugins > Crunchyroll Metadata`

| Configuração                 | Valor                   | Descrição                                                                                                     |
| ---------------------------- | ----------------------- | ------------------------------------------------------------------------------------------------------------- |
| **URL do FlareSolverr**      | `http://localhost:8191` | URL onde o FlareSolverr está rodando. Use `http://flaresolverr:8191` se ambos estiverem na mesma rede Docker. |
| **Nome do Container Docker** | `flaresolverr`          | O `--name` que você deu ao container. Deve corresponder exatamente.                                           |
| **URL do Chrome CDP**        | _(deixe vazio)_         | Avançado: Defina apenas se quiser sobrescrever a detecção automática da porta DevTools do Chrome.             |

Salve e reinicie o Jellyfin.

---

## Permissões do Docker Socket

### Linux (instalação systemd)

```bash
# Adicionar usuário jellyfin ao grupo docker
sudo usermod -aG docker jellyfin

# Reiniciar Jellyfin
sudo systemctl restart jellyfin
```

### Instalação Docker

Monte o Docker socket no container do Jellyfin:

```yaml
volumes:
  - /var/run/docker.sock:/var/run/docker.sock
group_add:
  - "docker"
```

---

## Solução de Problemas

| Problema                   | Solução                                                     |
| -------------------------- | ----------------------------------------------------------- |
| FlareSolverr não acessível | Verifique se está rodando: `docker ps \| grep flaresolverr` |
| Permission denied          | Adicione o usuário jellyfin ao grupo docker                 |
| CDP connection failed      | Verifique os logs: `docker logs flaresolverr`               |
| Metadados não carregam     | Verifique logs do Jellyfin por `[Crunchyroll]` e `CDP Auth` |

---

## Versões Recomendadas do FlareSolverr

| Versão  | Status                           |
| ------- | -------------------------------- |
| v3.4.6  | ✅ Testada e funcionando         |
| v3.3.21 | ✅ Testada e funcionando         |
| v2.x    | ❌ Não suportada (API diferente) |
