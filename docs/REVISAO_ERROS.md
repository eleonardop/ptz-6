# Revisão de erros (leitura de código, sem compilador)

Método: leitura linha a linha de todos os arquivos + simulação mental dos 40 testes + checagem de chaves/parênteses.
**Isto NÃO substitui compilar e executar.** Nada aqui foi rodado.

## Corrigidos
| # | Gravidade | Problema | Correção |
|---|---|---|---|
| 1 | Alta | Ao reconstruir uma aba (trocar de perfil, restaurar backup, adicionar câmera) o WPF lançava “elemento já tem um pai”, porque vários elementos são reaproveitados. As abas Mapeamento, Controle PTZ, Analógicos, Câmera e Backup parariam de atualizar. | `TabBase.Rebuild` desanexa a árvore antiga antes de reconstruir; `CameraTab` passou a usar `Rebuild()`. |
| 2 | Alta | Em UDP conectado no Windows, um aviso ICMP “porta inalcançável” faz o próximo envio falhar, inclusive o pacote de PARADA. | `SIO_UDP_CONNRESET` desativado no socket. |
| 3 | Média | Possível deadlock entre `ViscaClient` e `PtzController` ao desconectar. | `SetState` de `Disconnect` fora do lock. |
| 4 | Média | O botão “Testar” do foco nunca movia o foco (o valor era sobrescrito a cada ciclo de 16 ms). | Novo `PulseFocus` no controlador. |
| 5 | Média | Parada de foco enviada uma única vez; se o pacote UDP se perdesse, o foco seguiria ativo. | Parada de foco repetida (como Pan/Tilt/Zoom). |
| 6 | Média | Qualquer exceção ao ler o controle (não só SharpDX) repetia 60×/s uma caixa de erro. | Leitura trata qualquer exceção como “desconectado”; o ciclo de controle faz parada de emergência e registra o erro em vez de repetir a caixa. |
| 7 | Média | “Preset confirmado” podia ser marcado por uma conclusão de outro comando (ex.: parada do zoom). | Confirma só quando o ACK e a CONCLUSÃO têm o mesmo socket VISCA. Ainda é uma heurística. |
| 8 | Baixa | VID e PID apareciam trocados no Diagnóstico. | Corrigido. |
| 9 | Baixa | Editar o nome da câmera reconstruía o formulário e tirava o foco do campo seguinte (IP). | Lista não reconstrói o formulário ao ser repreenchida. |
| 10 | Baixa | Lista de presets mostrava “sem confirmação” mesmo depois da câmera confirmar; presets novos não apareciam nos botões rápidos. | Atualização automática; abas Controle PTZ e Presets reconstruídas ao abrir. |
| 11 | Baixa | Joystick e zoom da tela não faziam nada com a entrada “física” ativa, sem aviso. | Aviso na aba Controle PTZ. |

## Riscos que continuam (não corrigidos)
- **Nunca compilado.** Erros de sintaxe/tipos ainda são possíveis.
- Se a LJ510-30X-N não responder à consulta de versão, o cabeçalho mostrará “Desconectada” mesmo aceitando comandos; nesse caso desmarque “Verificar periodicamente” na aba Câmera.
- Procurar controle novo (a cada 2 s, sem controle conectado) roda na thread da tela; pode causar micro-travadas se o Windows demorar a listar dispositivos. Não medido.
- Em modo XInput os dois gatilhos compartilham um único eixo no DirectInput (Z); LT e RT podem não ser distinguíveis nesse modo.
- Tipo de conexão (Bluetooth × USB) não é informado automaticamente.
- Inverter o sentido do analógico de um lado ao outro passa direto de uma velocidade à outra, sem parar entre elas.
- Os `.csproj`/`.sln`/`app.manifest` foram reconstruídos por mim depois de eu sobrescrever os originais.

## Erros de compilação reportados pelo GitHub Actions (corrigidos)
| Arquivo | Erro | Causa | Correção |
|---|---|---|---|
| `Ui/Ux.cs` (janela de diálogo) | CS0103 `TextElement` | `TextElement` fica em `System.Windows.Documents`, que não era importado | Nome totalmente qualificado `System.Windows.Documents.TextElement` |
| `MainWindow.xaml.cs` | CS0103 `TextElement` | idem | idem |
| `Tabs/MappingTab.cs` | CS0246 `SolidColorBrush` | `SolidColorBrush` fica em `System.Windows.Media`, que esse arquivo não importa | `System.Windows.Media.SolidColorBrush` |
Antes: `Models.cs` sem `using PtzStudio.Core.Input;` (VirtualButtons).
Nenhum `using` novo foi adicionado (evita ambiguidades de nomes do WPF). Uma varredura de todos os arquivos não achou outro tipo do WPF/.NET usado sem o namespace correspondente.
