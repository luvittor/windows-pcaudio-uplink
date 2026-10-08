# Instruções do repositório

## Build e instalação

- Depois de qualquer `build`, `publish` ou geração de pacote, não substitua automaticamente os executáveis na instalação.
- Sempre pergunte explicitamente ao usuário se ele quer copiar/substituir os `.exe` na pasta de instalação.
- Se houver instâncias da aplicação abertas, pergunte também se o usuário autoriza encerrá-las antes da substituição; nunca finalize processos automaticamente.
- A pasta de instalação local fica configurada em `.env.local`, que não deve ser versionado.
- Use a variável `WINDOWS_PCAUDIO_INSTALL_DIR` desse arquivo para localizar a instalação.
