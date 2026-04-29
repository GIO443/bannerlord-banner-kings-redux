// Quick reflection inspector. Run with: dotnet script inspect-method.csx
// Prints the overloads of a method on a vanilla type.
#r "C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client/TaleWorlds.CampaignSystem.dll"
#r "C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client/TaleWorlds.Core.dll"
#r "C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord/bin/Win64_Shipping_Client/TaleWorlds.Library.dll"

using System;
using System.Reflection;
using TaleWorlds.CampaignSystem.GameComponents;

var t = typeof(DefaultPartySizeLimitModel);
foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
{
    if (m.Name.Contains("PartyMemberSize") || m.Name.Contains("PartyPrisonerSize"))
    {
        var ps = string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name));
        Console.WriteLine($"{m.DeclaringType.Name}.{m.Name}({ps}) -> {m.ReturnType.Name}");
    }
}
