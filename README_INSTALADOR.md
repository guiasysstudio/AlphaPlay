# AlphaPlay — Gerar instalador

## Arquivo correto do instalador

O arquivo correto para instalar em outro computador é:

```text
installer\output\AlphaPlay_Setup_1.0.0.exe
```

O arquivo abaixo **não é o instalador**:

```text
publish\AlphaPlay.exe
```

Esse `AlphaPlay.exe` é apenas o programa já publicado, usado como fonte para o Inno Setup montar o instalador.

## Antes de gerar

Instale o Inno Setup 6:

```text
https://jrsoftware.org/isdl.php
```

## Como gerar

Na pasta raiz do projeto, execute:

```powershell
.\gerar_instalador.bat
```

O script vai fazer tudo:

1. Publicar o AlphaPlay em `publish`.
2. Chamar o Inno Setup.
3. Gerar o instalador final em `installer\output`.
4. Abrir a pasta do instalador final automaticamente.

## Versão

A versão atual fica nos dois arquivos:

```bat
set APP_VERSION=1.0.0
```

Arquivos:

```text
publicar_alpha.bat
gerar_instalador.bat
```

Quando for gerar uma nova versão, altere para:

```bat
set APP_VERSION=1.0.1
```

O nome final será:

```text
AlphaPlay_Setup_1.0.1.exe
```

## O que o instalador faz

- Pede permissão de administrador.
- Instala em `C:\Program Files\AlphaPlay` por padrão.
- Permite alterar a pasta de instalação.
- Cria atalho no Menu Iniciar.
- Pergunta se deseja criar atalho na Área de Trabalho.
- Pergunta no final se deseja executar o AlphaPlay.
- Leva junto os arquivos necessários do LibVLC gerados pelo `dotnet publish`.
