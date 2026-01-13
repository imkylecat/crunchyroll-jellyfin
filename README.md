# Crunchyroll Metadata Plugin for Jellyfin

<p align="center">
  <img src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/branding/SVG/icon-transparent.svg" alt="Jellyfin Logo" width="100">
</p>

Um plugin para o Jellyfin que busca metadados de animes diretamente da Crunchyroll, com suporte inteligente para mapeamento de temporadas e episódios.

## ✨ Recursos

- **Metadados de Séries**: Título, descrição, ano de lançamento, gêneros e classificação etária
- **Metadados de Temporadas**: Títulos e descrições das temporadas
- **Metadados de Episódios**: Título, descrição, duração e data de exibição
- **Imagens**: Posters, backdrops e thumbnails de episódios
- **Suporte Multi-idioma**: Português (Brasil), Inglês, Japonês e mais

### 🎯 Resolução de Problemas Comuns

#### Problema 1: Temporadas Separadas (AniDB)
Outros plugins como o AniDB tratam cada temporada como uma obra separada. Este plugin resolve isso ao:
- Mapear automaticamente temporadas do Jellyfin para temporadas da Crunchyroll
- Manter todas as temporadas organizadas sob uma única série

#### Problema 2: Numeração de Episódios
A Crunchyroll às vezes usa numeração contínua de episódios entre temporadas. Por exemplo:
- **Jujutsu Kaisen**: A temporada 2 começa no episódio 25 na Crunchyroll
- **No seu servidor**: Você organiza como Temporada 2, Episódio 1

Este plugin resolve isso através do **cálculo automático de offset**, garantindo que:
- `S02E01` no seu servidor → `Episódio 25` na Crunchyroll ✓
- `S02E02` no seu servidor → `Episódio 26` na Crunchyroll ✓

## 📦 Instalação

### Método 1: Instalação via Repositório (Recomendado) ⭐

A forma mais fácil de instalar o plugin é através do repositório de plugins do Jellyfin:

1. **Adicione o repositório**
   - Acesse o painel do Jellyfin: `Dashboard > Plugins > Repositories`
   - Clique em `+` para adicionar um novo repositório
   - Cole a URL do manifesto:
     ```
     https://raw.githubusercontent.com/ocnaibill/crunchyroll-jellyfin/main/manifest.json
     ```
   - Clique em `Save`

2. **Instale o plugin**
   - Vá para `Dashboard > Plugins > Catalog`
   - Procure por "Crunchyroll Metadata"
   - Clique em `Install`

3. **Reinicie o Jellyfin**
   ```bash
   # Linux (systemd)
   sudo systemctl restart jellyfin
   
   # Docker
   docker restart jellyfin
   ```

4. **Configure o plugin**
   - Vá para `Dashboard > Plugins > My Plugins > Crunchyroll Metadata`
   - Configure o idioma e opções de mapeamento
   - Salve as configurações

5. **Execute um scan da biblioteca**
   - Vá para a sua biblioteca de animes
   - Clique em `Scan Library`

### Método 2: Instalação Manual

1. **Baixe o plugin**
   - Vá para a página de [Releases](https://github.com/ocnaibill/crunchyroll-jellyfin/releases)
   - Baixe o arquivo `Jellyfin.Plugin.Crunchyroll.zip`

2. **Extraia e copie os arquivos para o diretório de plugins**
   
   | Sistema Operacional | Caminho |
   |---------------------|---------|
   | Linux | `/var/lib/jellyfin/plugins/Crunchyroll/` |
   | Windows | `C:\ProgramData\Jellyfin\Server\plugins\Crunchyroll\` |
   | macOS | `~/.local/share/jellyfin/plugins/Crunchyroll/` |
   | Docker | `/config/plugins/Crunchyroll/` |

   > **Nota**: Crie a pasta `Crunchyroll` se ela não existir.

3. **Reinicie o Jellyfin**

4. **Verifique a instalação**
   - Vá para `Dashboard > Plugins`
   - O plugin "Crunchyroll Metadata" deve aparecer na lista

### Método 3: Compilando do Código Fonte

1. **Clone o repositório**
   ```bash
   git clone https://github.com/ocnaibill/crunchyroll-jellyfin.git
   cd crunchyroll-jellyfin
   ```

2. **Compile o plugin**
   ```bash
   dotnet build -c Release
   ```

3. **Copie a DLL gerada**
   ```bash
   # O arquivo estará em:
   # Jellyfin.Plugin.Crunchyroll/bin/Release/net8.0/Jellyfin.Plugin.Crunchyroll.dll
   ```

4. Siga os passos 2-4 do Método 2

## ⚙️ Configuração

Após instalar o plugin, configure-o em `Dashboard > Plugins > Crunchyroll Metadata`:

### Idioma
- **Idioma Preferido**: Selecione o idioma para os metadados (padrão: Português Brasil)
- **Idioma de Fallback**: Idioma alternativo quando o preferido não está disponível

### Mapeamento de Temporadas e Episódios
- **Habilitar Mapeamento de Temporadas**: Mapeia automaticamente temporadas do Jellyfin para a Crunchyroll
- **Habilitar Mapeamento de Offset de Episódios**: Calcula automaticamente o offset quando a Crunchyroll usa numeração contínua

### Cache
- **Expiração do Cache**: Tempo em horas para manter os metadados em cache (padrão: 24h)

## 🔧 Uso

### Configurando uma Biblioteca de Animes

1. **Crie ou edite uma biblioteca de séries de TV**
   - Vá para `Dashboard > Libraries > Add Media Library`
   - Tipo: `Shows`
   - Nome: `Animes` (ou como preferir)

2. **Habilite o provedor Crunchyroll**
   - Na configuração da biblioteca, vá para `Metadata Downloaders`
   - Habilite `Crunchyroll` para Séries, Temporadas e Episódios
   - Arraste `Crunchyroll` para a posição desejada na ordem de prioridade
   
3. **Habilite o provedor de imagens**
   - Em `Image Fetchers`, habilite `Crunchyroll`

### Organização de Arquivos Recomendada

```
Animes/
├── Jujutsu Kaisen/
│   ├── Season 1/
│   │   ├── Jujutsu Kaisen - S01E01 - Ryomen Sukuna.mkv
│   │   ├── Jujutsu Kaisen - S01E02 - For Myself.mkv
│   │   └── ...
│   └── Season 2/
│       ├── Jujutsu Kaisen - S02E01 - Hidden Inventory.mkv
│       ├── Jujutsu Kaisen - S02E02 - Hidden Inventory 2.mkv
│       └── ...
├── Demon Slayer/
│   ├── Season 1/
│   │   └── ...
│   └── Season 2/
│       └── ...
```

### Vinculando Manualmente a um Anime

Se o plugin não encontrar automaticamente o anime correto:

1. Clique na série → `Edit Metadata`
2. Em `Identify`, busque pelo nome na Crunchyroll
3. Selecione o resultado correto
4. Clique em `Refresh Metadata`

## 🐛 Solução de Problemas

### O plugin não encontra minha série
- Verifique se o nome no Jellyfin corresponde ao nome na Crunchyroll
- Tente buscar manualmente usando "Identify"
- Verifique se a série está disponível na Crunchyroll

### Metadados em idioma errado
- Verifique as configurações de idioma do plugin
- Alguns animes podem não ter metadados em todos os idiomas
- O plugin usará o idioma de fallback quando necessário

### Episódios não correspondem
- Certifique-se de que "Habilitar Mapeamento de Offset de Episódios" está ativado
- Verifique se a numeração local segue o padrão (cada temporada começa em E01)
- Em casos problemáticos, você pode definir manualmente o ID do episódio

### Logs para Debug
Ative logs detalhados em `Dashboard > Logs` e procure por entradas com `Crunchyroll`.

## 🔄 Atualizações

Se você instalou via repositório (Método 1), o Jellyfin irá notificá-lo automaticamente quando houver atualizações disponíveis. Basta ir em `Dashboard > Plugins > My Plugins` e clicar em `Update` quando disponível.

## 🤝 Contribuindo

Contribuições são bem-vindas! Por favor:

1. Faça um fork do repositório
2. Crie uma branch para sua feature (`git checkout -b feature/MinhaFeature`)
3. Commit suas mudanças (`git commit -m 'Adiciona MinhaFeature'`)
4. Push para a branch (`git push origin feature/MinhaFeature`)
5. Abra um Pull Request

## 📄 Licença

Este projeto está licenciado sob a Licença MIT - veja o arquivo [LICENSE.md](LICENSE.md) para detalhes.

## ⚠️ Aviso Legal

Este plugin não é afiliado, endossado ou patrocinado pela Crunchyroll ou pela Sony. 
"Crunchyroll" é uma marca registrada da Sony Group Corporation.

Este plugin usa dados disponíveis publicamente e não fornece acesso a conteúdo premium ou protegido por direitos autorais.

## 🙏 Agradecimentos

- [Jellyfin](https://jellyfin.org/) - O servidor de mídia open-source
- Comunidade de desenvolvedores de plugins do Jellyfin
- Projetos de documentação não-oficial da API da Crunchyroll

---

<p align="center">
  Feito com ❤️ para a comunidade do Jellyfin
</p>
