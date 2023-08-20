FString is an extremely rough implementation of a compile-time generator for zero-allocation format strings with localization support. You can take a `$"..."` format string from your C#, copy-paste it into an .fstring file, and run the generator to produce a .cs file you can include into your application, like this:

```csharp
Tooltip_CanUseInTurns (int cooldownTurnsLeft) = "  $[color:#FF0000]In $[.medium]{cooldownTurnsLeft} turn(s)$[.regular]$[]";
```

produces

```csharp
    public struct Tooltip_CanUseInTurns : IFString {
        public string Name => "Tooltip_CanUseInTurns";
        public int cooldownTurnsLeft;

        public void EmitValue (ref FStringBuilder output, string id) {
            switch(id) {
                case "cooldownTurnsLeft":
                    output.Append(cooldownTurnsLeft);
                    return;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id));
            }
        }
        public void AppendTo (ref FStringBuilder output, FStringDefinition definition) => definition.AppendTo(ref this, ref output);
        public void AppendTo (StringBuilder output) {
            var fsb = new FStringBuilder(output);
            FStringTable.Default.Get(Name).AppendTo(ref this, ref fsb);
        }
        public override string ToString () {
            var output = new FStringBuilder();
            AppendTo(ref output, FStringTable.Default.Get(Name));
            return output.ToString();
        }
    }
```

which you use by constructing an instance of the struct and then calling `AppendTo` or `ToString`. The generator produces an xml string table at the same time, and you can load alternate string tables on demand for other cultures.

If you have multiple format strings that use the same arguments, you can group them:

```csharp
(CombatStat stat, float amount, string resetStyle) {
    FormatStat_Health = "$[.medium]$[color:red] {amount}%{resetStyle}";
    FormatStat_Stamina = "$[.medium]$(combat:Stamina|\uF800 {amount}){resetStyle}";
    FormatStat_Initiative = "$[.medium]$(combat:Initiative|\uF801 {amount}){resetStyle}";
    FormatStat_Default = "$[.medium]{amount}$[.regular] {stat}{resetStyle}";
}
```

For simple automatic integration, use the `.targets` file by adding an import like this to your `.csproj`:
```xml
  <Import Project="..\..\FString\FString.targets" />
```
And then add an ItemGroup pointing to your `.fstring` file(s):
```xml
  <ItemGroup>
    <FStringTable Include="FStrings\*.fstring" />
  </ItemGroup>
```
