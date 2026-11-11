# PTZ Studio — Manual (português)

## Instalação
1. Execute **PTZStudio-Setup.exe** (ou abra direto o **PTZStudio.exe**, que não precisa de instalação).
2. Não é necessário instalar Python, .NET ou qualquer outra ferramenta.

## Primeiros passos
1. Aba **Câmera**: informe o IP da câmera (LJ510-30X-N; só números, como 192.168.1.100) e clique em **DESCOBRIR PORTA E PROTOCOLO**: o programa testa as portas 1259 e 52381 nos dois protocolos e preenche o que a câmera respondeu. Depois clique em **USAR ESTA CÂMERA, CONECTAR E TESTAR**. Se nada responder, confira o IP, o cabo/Wi-Fi, o firewall e o manual da câmera.
2. Conecte o GameSir (Bluetooth ou USB). O topo da janela mostra o nome do controle quando ele é detectado.
3. Aba **Controle PTZ**: teste com o joystick virtual (mouse) e com o controle físico.
4. Aba **Analógicos**: ative **Pan/Tilt completo (Analógico)** e use **Calibrar automaticamente** para conferir a direção.
5. Aba **Mapeamento**: clique em **ASSISTENTE: MAPEAR TODOS OS BOTÕES** e aperte cada botão quando o programa pedir. As funções já atribuídas continuam nos mesmos botões do desenho.

## Cabeçalho (sempre visível)
Mostra o estado da câmera, do controle, o perfil e a entrada ativa.

## Parar a câmera
Não existe mais botão de parada de emergência nem “retomar controle”. A câmera para como sempre: **ao soltar o analógico ou o joystick virtual** (a parada é enviada na hora e reforçada por cerca de 2 segundos, porque a rede UDP pode perder pacotes). O programa também para sozinho se o controle for desconectado, se a câmera deixar de responder ou se a entrada ficar parada por mais de 0,6 s com a câmera em movimento. Dica: o botão “Parar” (aba Mapeamento) faz uma parada imediata.

## Velocidade Pan/Tilt e Zoom (aba Controle PTZ)
- **Velocidade Pan/Tilt:** controle deslizante contínuo de **1% a 100%**, valendo para o joystick físico e para o virtual. Cada **perfil guarda a sua própria velocidade**; ao trocar de perfil o controle se atualiza. A tela mostra a velocidade real enviada à câmera (a câmera só tem 24 passos de Pan e 20 de Tilt, e 1% sempre equivale ao passo mais lento).
- **Intensidade do zoom:** de 1% a 100% da velocidade máxima de zoom do perfil, para os botões Aproximar/Afastar da tela. O zoom **não** é afetado pela velocidade Pan/Tilt. A câmera tem só 8 níveis de zoom, então a tela mostra o nível real.
- O joystick virtual volta sozinho ao centro quando você solta o mouse/dedo, troca de janela ou de aba, e a câmera para.

## Imagem da câmera: brilho e exposição
- Card **Imagem da câmera** (Controle PTZ). Ao conectar, o programa **pergunta à câmera** quais ajustes ela suporta; só os confirmados aparecem. Se a câmera não responder, nada é enviado e a tela explica (botão **Detectar ajustes de imagem** para tentar de novo).
- **Brilho** = o “ajuste de vídeo” da câmera (comando VISCA 04 A1, valor absoluto). **Exposição** = compensação de exposição (VISCA 04 3E/0E/4E, de −7 a +7). Cada um tem − / + (segure para repetir), controle deslizante e ↺ (volta ao valor de quando foi detectado).
- No **Mapeamento** existem as funções **Brilho +/−** e **Exposição +/−**; segure o botão para ajustar e solte para parar. Tela e controle mexem nos mesmos valores.
- Não implementados: ganho, obturador, íris e modo de exposição (automático/manual). Mudar o modo alteraria o comportamento da câmera ao vivo; podem ser acrescentados se você quiser.
- O suporte na LJ510-30X-N não pôde ser verificado sem a câmera: o fabricante informa brilho, íris e obturador automático/manual, mas não publica a lista de comandos. Por isso o programa confirma tudo com a própria câmera.

## Pan/Tilt completo (Analógico)
Aba **Analógicos**. Um único analógico controla Pan e Tilt ao mesmo tempo: cima/baixo = Tilt, esquerda/direita = Pan, diagonais movem os dois, centro = para. Quanto mais inclinado, mais rápido, dentro dos limites que você definir. Ajustes: zona morta, sensibilidade, velocidade máxima de Pan e de Tilt (independentes), inversão dos eixos X e Y, aceleração e desaceleração. As opções separadas de Pan e Tilt continuam nas abas Analógicos e Sensibilidade.

## Mapeamento do controle
- Escolha **Frente** ou **Traseira**, clique no botão do desenho, escolha a função e clique em **Aplicar**.
- O desenho foi traçado a partir das fotos do GameSir Nova 2 Lite: analógicos, direcional, ABXY, LB/RB, LT/RT, View, Menu, Home, Screenshot, M e, na traseira, L4 e R4.
- Perfis já existentes não são alterados (as funções “Parada de emergência” e “Alternar nível de velocidade”, que deixaram de existir, são removidas das atribuições).
- Botões com função mostram um **selo azul** e uma etiqueta com o nome da função; botões pressionados acendem em verde; o ponto azul mostra a posição do analógico.
- **DETECTAR BOTÃO**: selecione o botão no desenho, clique em DETECTAR BOTÃO e aperte o botão físico. Faça isso para M, Screenshot, Home, L4 e R4, que não têm número fixo no Windows.
- L4, R4 e M só aparecem se o Windows os enxergar no modo de conexão em uso. Se não aparecerem, veja a aba **Diagnóstico**.

## Movimento
A aba **Movimento** reúne os ajustes numéricos de Pan, Tilt e Zoom (antes divididos entre Sensibilidade e Zoom). A velocidade Pan/Tilt do perfil (1% a 100%) fica na aba Controle PTZ. **Restaurar padrões deste perfil** volta só os ajustes de movimento do perfil ativo aos valores de fábrica (botões, eixos e presets não mudam). A tela avisa quando um ajuste pode atrapalhar, como zona morta muito baixa ou filtro de ruído muito alto; seus valores nunca são alterados sozinhos.

## Presets
O preset é gravado na **memória da câmera** por comando VISCA. O programa só marca como confirmado quando a câmera responde; antes disso aparece como “aguardando confirmação”.

## Ao lado do relógio (bandeja do Windows)
- Ao **minimizar**, o PTZ Studio some da barra de tarefas e fica como um ícone ao lado do relógio. **Tudo continua funcionando**: controle físico, joystick virtual, presets, reconexão e os ajustes de imagem.
- **Clique no ícone** para abrir de novo. Botão direito: Abrir e Sair. Passar o mouse mostra o estado da câmera e do controle.
- O botão **fechar (X)** também só esconde a janela, para evitar fechar sem querer durante a transmissão. Para sair de verdade: botão direito no ícone > **Sair do PTZ Studio**.
- Com a janela escondida, o programa avisa por um balão quando a câmera perde a conexão.
- Abrir o programa de novo enquanto ele está na bandeja apenas traz a janela original (só uma cópia pode controlar a câmera).
- Para mudar esse comportamento: aba **Backup > Janela e bandeja do Windows**.

## Abertura
O programa abre a janela primeiro e só depois liga o controle e a câmera; as abas são criadas quando você as abre; a procura do controle e o backup automático rodam em segundo plano. A aba **Diagnóstico** mostra o tempo de abertura medido no seu computador.

## Desempenho
O programa foi feito para ser leve: com a janela escondida ou minimizada, as telas param de se atualizar; o controle dos movimentos tem prioridade sobre o desenho da tela; o processo roda com prioridade um pouco acima do normal (sem atrapalhar o OBS) e fora do “modo de economia” do Windows 11, que deixaria os movimentos irregulares quando minimizado.

## Registros
O programa **não** mostra nem guarda histórico de comandos (TX/RX) e não grava arquivos de registro contínuos. Na aba **Diagnóstico** ficam apenas os últimos erros de comunicação (na memória) e o botão **Copiar diagnóstico**, para pedir ajuda. Só falhas inesperadas do próprio programa vão para um pequeno arquivo `erros.log` (limite de 200 KB).

## Windows 7
O programa foi feito para Windows 10 e 11. O .NET 8, que ele usa, **não é oficialmente suportado no Windows 7**: no seu PC ele abriu e funcionou na tela, mas isso não é garantido. Os ícones agora são desenhados pelo próprio programa (a fonte de ícones do Windows 10 não existe no Windows 7). Se possível, use o Windows 10 ou 11 na transmissão. Também confirme que o GameSir é reconhecido no Windows 7 (o Bluetooth do Windows 7 costuma ser limitado; prefira o cabo USB).

## Backup
Aba **Backup**: criar backup, restaurar, exportar e importar configurações. Um backup automático é feito ao abrir o programa.

## Onde ficam os dados
Pasta `%AppData%\PTZStudio` (configurações e backups). Suas configurações antigas continuam valendo: as novas opções entram com valores que não mudam o comportamento anterior.
