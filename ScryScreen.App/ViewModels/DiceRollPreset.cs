using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ScryScreen.App.ViewModels;

public sealed partial class DiceRollPresetViewModel : ObservableObject
{
	public DiceRollPresetViewModel(string expression, string displayName, string iconKey)
	{
		Expression = expression;
		DisplayName = displayName;
		IconKey = iconKey;
		IsPercent = string.Equals(iconKey, "mdi-percent", StringComparison.Ordinal);
	}

	[ObservableProperty]
	private string expression;

	[ObservableProperty]
	private string iconKey;

	[ObservableProperty]
	private bool isPercent;

	[ObservableProperty]
	private string displayName;

	partial void OnExpressionChanged(string value)
	{
		var key = DiceRollerViewModel.GetPresetIconKeyForExpression(value);
		IsPercent = string.Equals(key, "mdi-percent", StringComparison.Ordinal);
		if (!string.Equals(IconKey, key, StringComparison.Ordinal))
		{
			IconKey = key;
		}
	}
}
