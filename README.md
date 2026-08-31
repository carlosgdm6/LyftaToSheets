# LyftaToSheets

> Automatiza a exportação dos meus treinos, cargas e repetições do [Lyfta](https://lyfta.app/) para o Google Sheets através da API do Lyfta.

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet\&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-13-239120?logo=csharp\&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![Google Sheets API](https://img.shields.io/badge/Google%20Sheets%20API-34A853?logo=google-sheets\&logoColor=white)](https://developers.google.com/sheets/api)
[![Lyfta API](https://img.shields.io/badge/Lyfta%20API-API-000000)](https://www.lyfta.app/)

---

## 📖 Sobre

O **LyftaToSheets** é uma aplicação desenvolvida para automatizar o processo de transferência dos meus dados de treino do **Lyfta** para o **Google Sheets**.

Utilizo o Lyfta para registar os meus treinos no ginásio, incluindo:

* 🏋️ Exercícios
* ⚖️ Cargas utilizadas
* 🔢 Repetições
* 📊 Séries
* 📅 Histórico dos treinos

Apesar de utilizar o Lyfta durante o treino, mantenho também os meus registos no Google Sheets para acompanhar a minha evolução e partilhar a informação com o meu coach.

O problema era que tinha de **copiar manualmente todos os dados do Lyfta para o Sheets** depois de cada treino.

O LyftaToSheets resolve esse problema através da API gratuita do Lyfta, permitindo obter os dados automaticamente e enviá-los para o Google Sheets.

### ⚡ Resultado

O que anteriormente exigia vários minutos de trabalho manual passou a ser feito em **apenas dois cliques**.

---

## 🔄 Como funciona

```text
┌───────────────┐
│     Lyfta     │
│               │
│    Treinos    │
│ Cargas / Reps │
└───────┬───────┘
        │
        │ Lyfta API
        ▼
┌──────────────────┐
│   LyftaToSheets  │
│                  │
│  Obter dados     │
│       ↓          │
│  Processar dados │
│       ↓          │
│  Estruturar dados│
└────────┬─────────┘
         │
         │ Google Sheets API
         ▼
┌──────────────────┐
│   Google Sheets  │
│                  │
│ Histórico treino │
└──────────────────┘
```

### Fluxo

1. Registo o treino normalmente no **Lyfta**.
2. O **LyftaToSheets** comunica com a API do Lyfta.
3. Os dados do treino são obtidos.
4. Os dados são processados e organizados.
5. A aplicação comunica com a Google Sheets API.
6. O treino é inserido automaticamente no Google Sheets.

---

## ✨ Funcionalidades

* [x] Obter treinos através da API do Lyfta
* [x] Obter exercícios
* [x] Obter cargas
* [x] Obter repetições
* [x] Processar os dados dos treinos
* [x] Exportar dados para Google Sheets
* [x] Automatizar o processo de registo
* [x] Exportação através de dois cliques
* [ ] Detecção de treinos já exportados
* [ ] Prevenção de duplicados
* [ ] Sincronização automática
* [ ] Sistema de logs
* [ ] Estatísticas de progresso

---

## 🛠️ Tecnologias

| Tecnologia            | Utilização                         |
| --------------------- | ---------------------------------- |
| **C#**                | Linguagem principal                |
| **.NET**              | Framework da aplicação             |
| **Lyfta API**         | Obtenção dos dados dos treinos     |
| **Google Sheets API** | Escrita dos dados no Google Sheets |
| **REST API**          | Comunicação com serviços externos  |
| **JSON**              | Estrutura dos dados                |

---

## 🎯 Motivação

Este projecto nasceu de uma necessidade real.

Durante o treino, o **Lyfta** é mais prático para registar as séries, cargas e repetições. No entanto, manter essa informação também no Google Sheets significava fazer o mesmo trabalho duas vezes.

Em vez de alterar o meu método de treino ou deixar de manter o histórico no Sheets, criei uma pequena integração entre os dois sistemas.

### Antes

```text
Treino
   ↓
Registar no Lyfta
   ↓
Abrir Google Sheets
   ↓
Copiar exercícios
   ↓
Copiar cargas
   ↓
Copiar repetições
   ↓
Organizar os dados
   ↓
Guardar
```

### Agora

```text
Treino
   ↓
Registar no Lyfta
   ↓
LyftaToSheets
   ↓
2 cliques
   ↓
Google Sheets actualizado
```

---

## 📊 Exemplo

Um treino registado no Lyfta pode conter dados como:

| Exercício        | Série | Carga | Repetições |
| ---------------- | ----: | ----: | ---------: |
| Supino Inclinado |     1 | 80 kg |         10 |
| Supino Inclinado |     2 | 80 kg |          8 |
| Supino Inclinado |     3 | 75 kg |          9 |
| Remada           |     1 | 70 kg |         10 |
| Remada           |     2 | 70 kg |          9 |

O **LyftaToSheets** trata da transformação destes dados e da sua exportação para a estrutura definida no Google Sheets.

---

## 🔌 Integrações

### Lyfta

O Lyfta é a origem dos dados.

A aplicação utiliza a API gratuita disponibilizada pelo Lyfta para obter os dados necessários dos treinos.

### Google Sheets

O Google Sheets é utilizado como destino dos dados.

Através da **Google Sheets API**, os dados processados pelo LyftaToSheets são escritos automaticamente na folha de cálculo.

---

## ⚙️ Configuração

### Pré-requisitos

Antes de executar o projecto, é necessário ter:

* [.NET SDK](https://dotnet.microsoft.com/download)
* Uma conta Google
* Acesso à Google Sheets API
* Credenciais da Google API
* Acesso à API do Lyfta

### Clonar o projecto

```bash
git clone https://github.com/USERNAME/LyftaToSheets.git
cd LyftaToSheets
```

### Restaurar dependências

```bash
dotnet restore
```

### Configuração

As credenciais e configurações necessárias devem ser definidas localmente.

Exemplo:

```json
{
  "Lyfta": {
    "ApiKey": "YOUR_API_KEY"
  },
  "Google": {
    "SpreadsheetId": "YOUR_SPREADSHEET_ID"
  }
}
```

> ⚠️ **Nunca coloques chaves de API, tokens ou outras credenciais directamente no repositório.**

### Executar

```bash
dotnet run
```

---

## 🔐 Segurança

O projecto utiliza serviços externos que requerem autenticação.

Por esse motivo, informações sensíveis como:

* API Keys
* Access Tokens
* Client Secrets
* IDs privados

devem ser mantidas fora do código-fonte e do controlo de versões.

O ficheiro `.gitignore` deve garantir que ficheiros de configuração locais e credenciais não são enviados para o Git.

---

## 🗺️ Roadmap

O projecto encontra-se em desenvolvimento e algumas funcionalidades poderão ser adicionadas futuramente.

### Próximos passos

* [ ] Melhorar a gestão de erros
* [ ] Implementar detecção de duplicados
* [ ] Permitir seleccionar quais os treinos a exportar
* [ ] Criar histórico de sincronizações
* [ ] Adicionar logs
* [ ] Melhorar a interface
* [ ] Automatizar sincronizações
* [ ] Adicionar estatísticas
* [ ] Permitir diferentes configurações de Google Sheets

---

## 🤝 Contribuição

Este é principalmente um projecto pessoal, mas sugestões, melhorias e contribuições são bem-vindas.

Se encontrares algum problema ou tiveres uma sugestão, podes abrir uma **Issue** no repositório.

---

## 📄 Licença

Este projecto é desenvolvido para fins pessoais e de aprendizagem.

A licença poderá ser definida posteriormente.

---

<p align="center">
  <strong>LyftaToSheets</strong>
  <br>
  Registar uma vez. Sincronizar automaticamente.
</p>
