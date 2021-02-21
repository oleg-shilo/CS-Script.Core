//css_inc cmd
using static System.Environment;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using System;

(string version,
 string lnx_version,
 string changes) = vs.parse_release_notes(args.FirstOrDefault() ?? Path.GetFullPath(@"..\..\release_notes.md"));

Console.WriteLine("===========================");
Console.WriteLine("Version: " + version);
Console.WriteLine("Debian Version: " + lnx_version);
Console.WriteLine("===========================");

@"..\..\CSScriptLib\src\CSScriptLib\CSScriptLib.csproj".set_version(version, changes);
@"..\..\csws\csws.csproj".set_version(version, changes);
@"..\..\cscs\cscs.csproj".set_version(version, changes);
@"..\..\css\Properties\AssemblyInfo.cs".set_version_old_proj_format(version);

static class methods
{
    public static void set_version_old_proj_format(this string project, string version)
    {
        version = version.strip_text();

        var content = File.ReadAllLines(project)
                          .Where(x => !x.StartsWith("[assembly: AssemblyVersion") && !x.StartsWith("[assembly: AssemblyFileVersion"))
                          .Concat(new[]{
                            $"[assembly: AssemblyVersion(\"{version}\")]{NewLine}" +
                            $"[assembly: AssemblyFileVersion(\"{version}\")]"});

        File.WriteAllLines(project, content);
    }

    public static void set_version(this string project, string version, string changes)
    {
        project.set_element("Version", version);
        project.set_element("FileVersion", version.strip_text());
        project.set_element("PackageReleaseNotes", changes);
        project.set_element("AssemblyVersion", version.strip_text());
    }

    public static void set_element(this string project, string elementName, string value)
    {
        var doc = XDocument.Load(project);
        doc.Descendants()
           .First(x => x.Name == elementName)
           .SetValue(value);
        doc.Save(project);
    }
}