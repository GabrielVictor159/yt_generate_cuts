namespace YT.Generate.Cuts.Domain.Enums;

/// <summary>
/// Regras de fluxo dos status de corte, num lugar só.
/// </summary>
/// <remarks>
/// Existe por causa de um risco concreto: o teto de cortes por canal conta os
/// cortes "pendentes". Se essa conta ficar espalhada como
/// <c>Status != CutStatusEnum.Publish</c> em cada consulta, um status novo entra
/// no sistema sem ser contado em algum lugar — e o freio que impede o fluxo de
/// crescer sem fim passa a ter um furo silencioso. Centralizando, adicionar um
/// status é mexer aqui.
/// </remarks>
public static class CutStatus
{
    /// <summary>
    /// Status que ainda demandam trabalho e, por isso, ocupam vaga no teto do
    /// canal. Tudo que não é terminal está aqui.
    /// </summary>
    public static readonly CutStatusEnum[] Pending =
    {
        CutStatusEnum.Created,
        CutStatusEnum.Process,
        CutStatusEnum.Edit,
    };

    /// <summary>Estados terminais: o trabalho no corte terminou.</summary>
    public static readonly CutStatusEnum[] Terminal =
    {
        CutStatusEnum.Publish,
    };

    /// <summary>O corte ainda demanda trabalho?</summary>
    public static bool IsPending(CutStatusEnum status) => !IsTerminal(status);

    public static bool IsTerminal(CutStatusEnum status) => Terminal.Contains(status);

    /// <summary>Status de entrada de cada etapa do fluxo.</summary>
    public const CutStatusEnum AwaitingFile = CutStatusEnum.Created;
    public const CutStatusEnum AwaitingEdition = CutStatusEnum.Process;
    public const CutStatusEnum AwaitingPublication = CutStatusEnum.Edit;
}
