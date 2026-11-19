using System.Windows;
using System.Windows.Controls;
using Notifs.Core;

namespace Notifs.App;

public sealed class RankBadge : Control
{
    public static readonly DependencyProperty RankProperty = DependencyProperty.Register(nameof(Rank), typeof(ImportanceRank), typeof(RankBadge), new PropertyMetadata(ImportanceRank.B));
    public ImportanceRank Rank { get => (ImportanceRank)GetValue(RankProperty); set => SetValue(RankProperty, value); }
    public RankBadge() => SetResourceReference(StyleProperty, "RankBadge");
}
