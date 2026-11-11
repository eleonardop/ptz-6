# Relatório de implementação — atualização 2

Base: o mesmo projeto C#/.NET 8 + WPF (`PtzStudio.Core` = lógica; `PtzStudio.App` = interface). Nada foi reescrito do zero.

## O que mudou
| Pedido | Implementação |
|---|---|
| Interface profissional | Tema escuro (grafite/preto com azul) em `Themes/Dark.xaml`; navegação lateral com ícones; cartões; cabeçalho com indicadores de câmera, controle, perfil e entrada; controles padronizados. |
| Controle fiel | `ControllerArt.cs` (contornos traçados das fotos) + `ControllerView.cs`: frente e traseira, áreas clicáveis (A/B/X/Y, direcional, analógicos e L3/R3, LB/RB, LT/RT, View, Menu, Home, Screenshot, M, L4, R4), selo e etiqueta nos botões com função. |
| Pan/Tilt completo | `AxisMap.FullPanTilt`, limite de diagonal no `InputInterpreter`, polaridade dos eixos por calibração, aba Analógicos com calibração automática e verificação de conflito com o zoom. As opções individuais foram mantidas. |
| Remover “Parar tudo” | Botão removido da aba Controle PTZ. A função interna `StopAll` continua existindo. |
| Emergência | Trava dura em duas camadas: `PtzController` (`Tx`, entradas ignoradas, `Tick` só repete paradas) e `ViscaClient` (`MotionLocked` + `ViscaCommands.IsSafeWhileLocked`). Faixa “EMERGÊNCIA ATIVADA” e botão “RETOMAR CONTROLE” com verificações; reconexão não libera a trava. |
| Sem registros técnicos | Removidos TX/RX, histórico de comandos, painel de registro, gravação contínua em arquivo e a propriedade `LogDirectory`. Mantidos o tratamento de erros e os últimos 30 erros na memória. |
| Presets | Lógica, quantidade e estrutura inalteradas; só a apresentação (e o bloqueio durante a emergência). |

## Arquivos novos
`Themes/Dark.xaml`, `Ui/ControllerArt.cs`, `Ui/ActionLabels.cs`, `tests/.../EmergencyTests.cs`.

## Limitações reais
- **Nada foi compilado nem executado** (o ambiente de desenvolvimento não tem .NET nem Windows). O primeiro build no GitHub Actions pode apontar erros de compilação ou de XAML (tema).
- As áreas clicáveis foram alinhadas por sobreposição com as fotos fornecidas, mas só a tela real mostra se estão boas.
- Ícones usam a fonte do Windows (Segoe MDL2 Assets); se algum aparecer vazio, é só cosmético.
- L4/R4/M/Screenshot/Home dependem do que o Windows expõe no modo de conexão do controle; não há garantia.
- A parada de emergência depende da rede: sem comunicação o programa não consegue garantir que a câmera a recebeu.
- Sem câmera: se ela não responder às consultas de verificação, o cabeçalho mostrará “Desconectada” e RETOMAR CONTROLE só liberará depois que a câmera responder ou se “Verificar periodicamente” estiver desligado na aba Câmera.

## Atualização 3 — desempenho e bandeja
| Item | Detalhe |
|---|---|
| Bandeja | `Ui/TrayIcon.cs` (API do Windows direta, sem Windows Forms), menu (Abrir, Emergência, Retomar, Sair), balões de aviso, ícone do programa em `Assets/app.ico`, minimizar/fechar para a bandeja (opcional, em Backup), instância única. |
| Prioridade do loop | O temporizador do controle usava prioridade `Background` do WPF (perde para mouse/desenho) → agora `Normal`. |
| Telas ocultas | Abas não se atualizam com a janela escondida/minimizada. |
| Sem efeitos pesados | Removidas as sombras (`DropShadowEffect`) do joystick e do ponto do analógico. |
| Procura de controle | Passou para segundo plano (não trava a tela). |
| Processo | Prioridade acima do normal e saída do modo de economia do Windows 11 (`ProcessTuning`). |
| Publicação | `PublishReadyToRun` (início mais rápido, menos compilação em tempo de uso); coletor de lixo sem thread de fundo. |
| Código sem uso removido | `IfClear`, `IsHold`, lista de erros duplicada no cliente VISCA, `Disconnect`, 2 pincéis. |

Não medido: consumo real de CPU/memória e tamanho do .EXE (dependem de compilar e rodar). Um programa WPF independente do .NET instalado costuma ter dezenas de MB; isso não afeta o uso de CPU/memória durante a operação.

## Atualização 4 — desempenho, velocidade em %, imagem
| Pedido | O que foi feito |
|---|---|
| 1. Abertura mais rápida | Causas prováveis (sem profiler, pois o ambiente de desenvolvimento não roda Windows): (a) o motor — procura do controle com varredura DirectInput síncrona, conexão com a câmera e início do ciclo — rodava **antes** da primeira tela; (b) as 9 abas eram montadas na abertura (e reconstruídas ao abrir); (c) o backup automático (compactar arquivo) rodava na abertura. Agora: motor inicia em `ContentRendered`; busca do controle em segundo plano; abas criadas sob demanda; backup em segundo plano. Medição real: aba Diagnóstico (“Tempo de abertura”). Não medido: custo da extração do .exe único e do .NET no Windows 7 — se continuar lento, a próxima medida seria distribuir a pasta publicada (sem arquivo único) pelo instalador. |
| 2. Sem emergência/retomar | Removidos: botão, faixa, menu da bandeja, `ActivateEmergency/TryResume/ReassertStop`, trava no cliente VISCA (`MotionLocked`, `IsSafeWhileLocked`), estados e testes. Mantida a parada de segurança automática (watchdog, controle desconectado, câmera sem resposta), agora chamada `SafetyStop`, e o reforço da parada. Atribuições antigas `ParadaEmergencia` e `AlternarVelocidade` são removidas ao carregar o perfil (os valores continuam no enum só para ler arquivos antigos). |
| 3/4. Velocidade 1–100% por perfil | `MotionSettings.PanTiltSpeedPercent` (por perfil, salvo no mesmo arquivo). Multiplica Pan e Tilt (nunca o Zoom) e respeita o limite da câmera. Perfis antigos: o nível padrão (35/65/100%) vira a porcentagem; campos antigos deixam de ser gravados. Níveis, botão “Alternar velocidade” e a seção da tela Movimento removidos. |
| 5. Zoom 1–100% | `MotionSettings.VirtualZoomPercent` (por perfil), aplicado ao zoom dos botões da tela sobre a velocidade máxima de zoom do perfil. A tela mostra o nível VISCA real (a câmera tem só 8). |
| 6. Sem “Centralizar” | Botão removido. O joystick volta ao centro ao soltar o mouse (captura do mouse), ao perder a captura, ao trocar de janela/aba e ao sair da tela; os valores virtuais são zerados nesses casos. |
| 7. Brilho e exposição | `CameraImage` (Core): detecta o suporte **perguntando à câmera** (consultas VISCA) e só então libera; brilho = “ajuste de vídeo” (04 A1, absoluto), exposição = compensação de exposição (04 3E/0E/4E). Tela: card “Imagem da câmera” (−/+ com repetição, controle deslizante, valor inicial). Controle: funções Brilho ±/Exposição ± (segurar repete). Mesma classe para os dois, valores sincronizados. |
| 8. Organização visual | Controle PTZ reorganizado (velocidade logo abaixo do joystick, imagem sob o zoom, entrada e presets à direita); sem vãos vazios na aba Câmera; botão do assistente com texto que cabe. |

### Limitações reais desta atualização
- **Nada foi compilado nem executado** (sem .NET/Windows no ambiente de desenvolvimento). Os testes novos (velocidade, migração, comandos e imagem com câmera simulada em UDP) só rodam no GitHub Actions.
- **Suporte da LJ510-30X-N a brilho/exposição não verificado** (sem a câmera nem o manual de comandos; o fabricante, SMTAV, informa “ajuste de vídeo: brilho”, íris e obturador automático/manual). O programa confirma com a câmera; se ela não responder, nada é enviado.
- A **faixa do brilho** varia por câmera e não há como consultá-la: começa em 0–14 e é aprendida ao usar.
- **Não implementados** (não pedidos ou arriscados ao vivo): ganho, obturador, íris e modo de exposição.
- O consumo medido por você no Windows 7 (CPU 0%, cerca de 152 MB) é típico de um programa WPF independente; esta atualização não tenta reduzir memória.
