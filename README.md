FString is an extremely rough implementation of a compile-time generator for zero-allocation format strings with localization support. You can take a `$"..."` format string from your C#, copy-paste it into an .fstring file, and run the generator to produce a .cs file you can include into your application, like this:

```csharp
Tooltip_CanUseInTurns (int cooldownTurnsLeft) = $"  $[color:#FF0000]In $[.medium]{cooldownTurnsLeft} turn(s)$[.regular]$[]";
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
    FormatStat_Health = $"$[.medium]$[color:red] {amount}%{resetStyle}";
    FormatStat_Stamina = $"$[.medium]$(combat:Stamina|\uF800 {amount}){resetStyle}";
    FormatStat_Initiative = $"$[.medium]$(combat:Initiative|\uF801 {amount}){resetStyle}";
    FormatStat_Default = $"$[.medium]{amount}$[.regular] {stat}{resetStyle}";
}
```

Basic support for switch statements is available, using syntax like the following:
```csharp
(ICombatParticipant Caster, ActionDefinition Definition, bool confirmed) {
    Action_CombatLog_Failed = switch (confirmed) {
        true = $"{Caster} failed to use {Definition.Name}: Confirmation failed";
        false = $"{Caster} failed to use {Definition.Name}: No valid targets";
    }
}
```

For simple automatic integration, use the `.targets` file by adding an import like this to your `.csproj`:
```xml
  <Import Project="..\..\FString\FString.targets" />
```
And then add an ItemGroup pointing to your `.fstring` file(s) along with the output of the compiler:
```xml
  <ItemGroup>
    <FStringTable Include="FStrings\*.fstring" />
    <!-- visual studio is bad software so we can't do this automatically for you -->
    <Compile Include="$(FStringOutputPath)\*.cs" />
  </ItemGroup>
```

The compiler produces a static class named `FStrings` containing a static method or struct for each of your strings.

At startup, you'll want to load an appropriate string table for the current culture, like so:

```csharp
        public static void LoadDefaultStringTable () {
            var culture = CultureInfo.CurrentCulture.ThreeLetterISOLanguageName;
            var path = Path.Combine(ApplicationDirectory, $"FStringTable_{culture}.xml");
            using (var stream = File.OpenRead(path))
                Squared.FString.FStringTable.Default = new Squared.FString.FStringTable(culture, stream);
        }
```