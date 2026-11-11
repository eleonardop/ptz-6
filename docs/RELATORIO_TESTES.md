# Relatório de testes

**Situação honesta: nada foi executado ainda.** Este ambiente não tem .NET nem Windows; a execução dos testes e a compilação Release acontecem no GitHub Actions.

## Testes automatizados (xUnit) — escritos, não executados
- Curvas, zona morta, aceleração/desaceleração, diagonais, inversão, zoom (`MotionTests`).
- Pacotes VISCA, respostas, cabeçalho VISCA over IP (`ViscaTests`).
- Controlador: movimento, parada, watchdog, preset, pulso, foco (`ControllerTests`).
- **Emergência (`EmergencyTests`):** trava dos pacotes, bloqueio de todas as fontes de movimento, bloqueio mesmo com entradas no neutro, recusa de retomada com câmera desconectada ou entradas fora do neutro, retomada sem reaproveitar comandos antigos, reenvio de paradas, filtro `IsSafeWhileLocked`.
- Pan/Tilt completo: vetor limitado a raio 1, diagonal dentro das velocidades máximas, polaridade dos eixos, perfis novos × arquivos antigos, novos botões no layout sem apagar chaves antigas.
- Armazenamento: salvar/carregar, backup, restauração, arquivo corrompido (`StorageTests`).

## Dependem da câmera e do controle reais (NÃO testados)
Comunicação VISCA com a LJ510-30X-N, movimentos reais de Pan/Tilt/Zoom, parada com movimento em andamento, perda e retorno da conexão de rede, reconhecimento do GameSir por USB e Bluetooth, botões M/Screenshot/Home/L4/R4, calibração automática do analógico.

## Dependem de olhar a tela (NÃO verificados)
Aparência do tema escuro, alinhamento das áreas clicáveis sobre o desenho, comportamento em resoluções diferentes, ícones.

## Bandeja e desempenho — NÃO testados
Ícone ao lado do relógio (aparecer, tooltip, menu, balões, reaparecer se o Explorer reiniciar), minimizar/fechar para a bandeja, instância única, controle funcionando com a janela escondida e com o OBS em uso, efeito do ajuste de prioridade. Só dá para confirmar no Windows. Automatizado: padrões das novas opções (`EmergencyTests`).

## Atualização 4 (melhorias da auditoria) — testes novos, NÃO executados
`ReliabilityTests`: reforço da parada (e que ele não cancela um zoom em andamento nem interrompe um preset), perfis padrão sem emergência em botão isolado, sanitização preservando ajustes válidos, restauração de padrões por perfil, migração do layout (inclusive troca entre dois botões), cliente VISCA em loopback (conexão, trava no fio, IP inválido) e descoberta de porta/protocolo.
Dependem do equipamento real: tudo que envolve a câmera LJ510-30X-N e o GameSir; assistente de mapeamento; bandeja; aparência no Windows 7.

## Atualização 4 (escritos, NÃO executados)
`SpeedAndImageTests`: velocidade Pan/Tilt em % (100/50/25/1%, limite da câmera, zoom independente, zoom virtual), persistência por perfil, migração de arquivo antigo (níveis → %, remoção de atribuições obsoletas), campos antigos não gravados, faixas 1–100, formato dos comandos VISCA de imagem, leitura das respostas, roteador (segurar brilho/exposição), e — com uma câmera simulada em UDP — detecção, passos, controle deslizante, recusa pela câmera, segurar/soltar e ausência de conexão. Removidos os testes da emergência. Dependem da câmera/controle reais (NÃO testados): resposta real da LJ510-30X-N aos comandos de imagem, efeito visível na imagem, parada ao soltar o joystick com a rede real, tempo de abertura no Windows 7.
