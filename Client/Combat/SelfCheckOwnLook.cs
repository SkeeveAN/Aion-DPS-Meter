using System.Globalization;
using AionDPS.Ui;

namespace AionDPS.Combat;

/// <summary>The template of the look "Own" of the DPS overlay: reading it, the number formats, the symbols that belong to the variables, the errors.</summary>
public static class SelfCheckOwnLook
{
    public static bool Run()
    {
        Console.WriteLine("[selftest] Own look (the template: fields, options, formats, symbols, errors):");
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            ParsedTemplate t = OverlayTemplate.Parse("%RANK%  %NAME%(16,b)  %DPS%(short,r8)\\t%SHARE%\n\\n%POWER%(short,+2,n) %TAKEN%");
            List<TemplateField> fields = t.Fields.ToList();
            TemplateField name = fields[1], dps = fields[2], power = fields[4];
            bool read = t.Errors.Count == 0 && fields.Count == 6 && name is { Var: TemplateVar.Name, Width: 16, Bold: true }
                && dps is { Var: TemplateVar.Dps, Short: true, Right: true, Width: 8 } && power is { Var: TemplateVar.Power, Short: true, Size: 2, NoSymbol: true }
                && t.Items.Count(i => i.Tab) == 1 && t.Items.Count(i => i.NewLine) == 2;
            Console.WriteLine($"  -> fields with width, bold, size, short, side and no-symbol are read; tab and both line breaks too: {read}");

            ParsedTemplate english = OverlayTemplate.Parse("%DAMAGE% %damage% %TAKEN% %POWER% %DEATHS% %RANK% %CLASS% %LEVEL% %SHARE% %HEAL% %IDPS%");
            bool aliases = english.Errors.Count == 0 && english.Fields.Count() == 11 && english.Fields.Take(2).All(f => f.Var == TemplateVar.Damage);
            Console.WriteLine($"  -> the variables are English, in either case: {aliases}");

            ParsedTemplate bad = OverlayTemplate.Parse("%DSP% %NAME%(16,zz) %DPS%(short");
            bool errors = bad.Errors.Count == 3 && bad.Errors[0] is { Kind: "Variable", Arg: "DSP" } && bad.Errors[1] is { Kind: "Option", Arg: "zz" } && bad.Errors[2].Kind == "Bracket";
            Console.WriteLine($"  -> an unknown variable, an unknown option and a missing bracket are told: {errors}");

            bool numbers = OverlayTemplate.Number(2_514_236, false) == "2.514.236" && OverlayTemplate.Number(2_514_236, true) == "2,514 M"
                && OverlayTemplate.Number(212_145, false) == "212.145" && OverlayTemplate.Number(212_145, true) == "212,1 k" && OverlayTemplate.Number(950, true) == "950";
            Console.WriteLine($"  -> long 2.514.236 and short 2,514 M; the combat power 212.145 and 212,1 k: {numbers}");

            var row = new PlayerRow(1) { Name = "Jinwoo", Damage = 2_514_236, Dps = 8300, SharePercent = 31.2, DamageTaken = 412_000, Healing = 1_384_500, CombatPower = 212_145, Deaths = 1, Rank = 1, Level = 55 };
            ParsedTemplate symbols = OverlayTemplate.Parse("%POWER%(short) %TAKEN%(short) %HEAL% %DEATHS% %POWER%(n) %TAKEN%(n,short)");
            string[] values = symbols.Fields.Select(f => OverlayTemplate.ValueOf(f, row)).ToArray();
            var empty = new PlayerRow(2) { Name = "Nobody" };
            bool idps = OverlayTemplate.ValueOf(OverlayTemplate.Parse("%IDPS%").Fields.First(), row) == "8.300" && OverlayTemplate.ValueOf(OverlayTemplate.Parse("%IDPS%(short)").Fields.First(), row) == "8,3 k";
            bool symbolsOk = idps && values[0] == "⚔ 212,1 k" && values[1] == "↓ 412,0 k" && values[2] == "+1.384.500" && values[3] == "☠ 1" && values[4] == "212.145" && values[5] == "412,0 k"
                && OverlayTemplate.ValueOf(OverlayTemplate.Parse("%POWER%").Fields.First(), empty) == "" && OverlayTemplate.ValueOf(OverlayTemplate.Parse("%TAKEN%").Fields.First(), empty) == "↓ –";
            Console.WriteLine($"  -> the swords, the arrow, the plus and the skull belong to their variables and go with the option n; nothing known gives nothing: {symbolsOk}");

            TemplateField round = OverlayTemplate.Parse("%NAME%(8,+1,short,r,b,n)").Fields.First();
            ParsedTemplate again = OverlayTemplate.Parse(round.ToText());
            bool roundTrip = round.ToText() == "%NAME%(8,b,+1,short,r,n)" && again.Fields.First() is { Width: 8, Bold: true, Size: 1, Short: true, Right: true, NoSymbol: true };
            ParsedTemplate caret = OverlayTemplate.Parse("ab %DPS%(short) cd");
            bool at = caret.FieldAt(5)?.Var == TemplateVar.Dps && caret.FieldAt(2) is null && caret.FieldAt(16) is null;
            Console.WriteLine($"  -> a field is written back as text; the cursor in a field selects it, between fields it does not: {roundTrip && at}");
            return read && aliases && errors && numbers && symbolsOk && roundTrip && at;
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }
}
