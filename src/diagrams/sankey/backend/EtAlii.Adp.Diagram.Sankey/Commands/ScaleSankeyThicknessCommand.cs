using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>Draws every bar and band thicker (<paramref name="Steps"/> above zero) or thinner (below), by one factor per step.</summary>
public sealed record ScaleSankeyThicknessCommand(string BodyPath, int Steps) : ICommand;
