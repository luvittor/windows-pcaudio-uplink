# Instruções do repositório

## Build e instalação

- Nunca criar commit nem executar push sem autorização explícita do usuário para aquele commit específico, mesmo quando a alteração já foi testada e faz parte do plano em andamento.
- Antes de pedir autorização, mostrar resumidamente o que entrará no commit e o resultado dos testes.
- Uma autorização para implementar, continuar, instalar ou testar não implica autorização para commit ou push.

- Depois de qualquer `build`, `publish` ou geração de pacote, não substitua automaticamente os executáveis na instalação.
- Sempre pergunte explicitamente ao usuário se ele quer copiar/substituir os `.exe` na pasta de instalação.
- Se houver instâncias da aplicação abertas, pergunte também se o usuário autoriza encerrá-las antes da substituição; nunca finalize processos automaticamente.
- Na mesma confirmação da instalação, pergunte se o usuário autoriza inicializar o tray após a substituição; nunca inicie a aplicação automaticamente sem essa autorização.
- A pasta de instalação local fica configurada em `.env.local`, que não deve ser versionado.
- Use a variável `WINDOWS_PCAUDIO_INSTALL_DIR` desse arquivo para localizar a instalação.
