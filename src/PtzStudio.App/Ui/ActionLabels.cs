using PtzStudio.Core.Models;

namespace PtzStudio.App.Ui;

/// <summary>Nomes curtos das funções, usados nas etiquetas do desenho do controle e nos botões de acesso rápido.</summary>
internal static class ActionLabels
{
    public static string Short(ButtonAction a, int p) => a switch
    {
        ButtonAction.PanEsquerda => "Pan ←",
        ButtonAction.PanDireita => "Pan →",
        ButtonAction.TiltCima => "Tilt ↑",
        ButtonAction.TiltBaixo => "Tilt ↓",
        ButtonAction.ZoomAproximar => "Zoom +",
        ButtonAction.ZoomAfastar => "Zoom −",
        ButtonAction.FocoProximo => "Foco perto",
        ButtonAction.FocoDistante => "Foco longe",
        ButtonAction.Parar => "Parar",
        ButtonAction.SalvarPreset => $"Salvar P{p}",
        ButtonAction.AcionarPreset => $"Preset {p}",
        ButtonAction.AlternarPerfil => "Perfil",
        ButtonAction.Home => "Home",
        ButtonAction.AutoFoco => "Autofoco",
        ButtonAction.AlternarEntrada => "Entrada",
        ButtonAction.BrilhoMais => "Brilho +",
        ButtonAction.BrilhoMenos => "Brilho −",
        ButtonAction.ExposicaoMais => "Exposição +",
        ButtonAction.ExposicaoMenos => "Exposição −",
        _ => ""
    };
}
