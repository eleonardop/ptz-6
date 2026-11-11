# Auditoria e melhorias — situação de cada item

Tudo abaixo foi escrito e conferido por leitura; **nada foi compilado nem executado** (ver RELATORIO_TESTES.md).

| Item | O que foi feito | Onde |
|---|---|---|
| P1 Parada reforçada | A parada de Pan/Tilt, Zoom e Foco é repetida por cerca de 2 s (30, 90, 250, 600, 1200 e 2000 ms), só no eixo parado; cancelada ao acionar preset/Home (não interrompe o deslocamento). | `PtzController` (`StopRepeat`) |
| P2 Emergência em botão isolado | Perfis novos: emergência = L3 + R3. Perfis existentes não mudam. | `Models.MakeProfile` |
| P3 Gravação a cada movimento | `Save()` agrupa as alterações (600 ms); `Flush()` ao sair e antes de backup. | `AppEngine` |
| P4 Validação | IP só numérico (sem DNS na tela); `SettingsSanitizer` corrige só valores impossíveis; ajustes válidos do usuário (ex.: zona morta 0) ficam intactos. | `ViscaClient`, `SettingsSanitizer`, `CameraTab` |
| P5 Diálogos no ciclo de controle | Diálogos de detecção agora abrem fora do `Tick`. Também: o botão recém-detectado não executa mais a própria função. | `MappingTab`, `PresetsTab`, `AppEngine` |
| M1 Descobrir porta/protocolo | `CameraProbe` + botão na aba Câmera. | `CameraProbe`, `CameraTab` |
| M2 Mapeamento guiado | Assistente que pede cada botão e leva as funções junto (`LayoutMigration`). | `MappingTab`, `LayoutMigration` |
| M3 Ajustes duplicados | Zoom + Sensibilidade viraram **Movimento**; removidas as zonas mortas e inversões repetidas da aba Analógicos; abas se reconstroem ao abrir (valores sempre atuais). | `MotionTab`, `AnalogTab`, `MainWindow` |
| M4 Testes de integração | Cliente VISCA e descoberta em loopback UDP; trava no fio; reforço da parada; migração; sanitização. | `ReliabilityTests` |
| M5 Largura da janela | Colunas do Controle PTZ quebram linha; indicadores do topo quebram linha; coluna do Mapeamento menor. | `PtzTab`, `MainWindow`, `MappingTab` |
| Prints: ícones quadrados | Ícones vetoriais próprios (não dependem de fonte). | `Ux.Icon` |
| Prints: slider do zoom sumido | `SliderRow` com modo empilhado. | `Ux`, `PtzTab` |
| Prints: valores arriscados | Avisos (sem alterar valores) e botão "Restaurar padrões deste perfil". | `AxisSettingsPanel`, `MotionTab` |
| Prints: prévia branca, legenda sobre etiquetas, LT/RT pequenos, botões da lista, aviso eterno | Prévia escura; legenda removida; LT/RT maiores; botões alinhados; avisos somem em 8 s. | vários |

## Propostos e **não** feitos (de propósito)
- Ciclo de controle em thread própria: medir antes (grupo "opcional").
- Trocar o SharpDX: sem evidência de problema.
- Assinatura digital do .exe, iniciar com o Windows, compactar o .exe: opcionais, sem pedido.
- Portar para .NET Framework 4.8 (para Windows 7 oficial): decisão grande; só se o PC da transmissão precisar mesmo ser Windows 7.
