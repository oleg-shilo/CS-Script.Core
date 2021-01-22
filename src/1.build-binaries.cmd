echo off

set vs_edition=Community
if exist "C:\Program Files (x86)\Microsoft Visual Studio\2019\%vs_edition%" (
    echo Visual Studio 2019 (Community)
) else (
    set vs_edition=Professional
    echo Visual Studio 2019 (PRO)
)

set PATH=%PATH%;%%\out\ci\
set target=net5.0
md "out\Windows"
md "out\Linux"
md "out\Linux\-self"
md "out\Linux\-self\-exe"
md "out\Linux\-self\-test"
md "out\Windows\-self"
md "out\Windows\-self\-exe"
md "out\Windows\-self\-test"

rem in case some contentis already there
del /S /Q "out\Linux\"
del /S /Q "out\Windows\"
del "out\cs-script.win.7z"
del "out\cs-script.linux.7z"


set msbuild="C:\Program Files (x86)\Microsoft Visual Studio\2019\%vs_edition%\MSBuild\Current\Bin\MSBuild.exe"
%msbuild% ".\css\css (win launcher).csproj" -p:Configuration=Release -t:rebuild
copy .\css\bin\Release\css.exe ".\out\Windows\css.exe"


echo =====================
echo Building (cd: %cd%)
echo =====================

del .\out\*.*nupkg 

cd BuildServer
echo ----------------
echo Building build.dll from %cd%
echo ----------------
dotnet publish -c Release 

cd ..\cscs
echo ----------------
echo Building cscs.dll from %cd%
echo ----------------
dotnet publish -c Release -f %target% -o "..\out\Windows\console"

echo ----------------
echo Building cscs.dll (Linux) from %cd%
echo ----------------
dotnet publish -c Release -f %target% -r linux-x64 --self-contained false -o "..\out\Linux"

cd ..\csws
echo ----------------
echo Building csws.dll from %cd%
echo ----------------
dotnet publish -c Release -f %target%-windows -o "..\out\Windows\win"


cd ..\CSScriptLib\src\CSScriptLib
echo ----------------
echo Building CSScriptLib.dll from %cd%
echo ----------------
dotnet build -c Release

cd ..\..\..

echo =====================
echo Aggregating (cd: %cd%)
echo =====================
copy "out\Windows\win" "out\Windows" /Y
copy "out\Windows\console" "out\Windows" /Y
del "out\Linux\*.pdb" 
del "out\Windows\*.pdb" 
rd "out\Windows\win" /S /Q
rd "out\Windows\console" /S /Q


cd out\Windows

echo >  -code.header    using System;
echo >> -code.header    using System.IO;
echo >> -code.header    using System.Collections;
echo >> -code.header    using System.Collections.Generic;
echo >> -code.header    using System.Linq;
echo >> -code.header    using System.Reflection;
echo >> -code.header    using System.Diagnostics;
echo >> -code.header    using static dbg;
echo >> -code.header    using static System.Environment;

echo off
cd ..\..

copy "out\static_content\-code.header" "out\Linux" 
copy "out\static_content\-code.header" "out\Windows" 

copy "out\static_content\-self\*" "out\Windows\-self\" 
copy "out\static_content\-self\*" "out\Linux\-self\" 

copy "out\static_content\-self\-exe\*" "out\Windows\-self\-exe\" 
copy "out\static_content\-self\-exe\*" "out\Linux\-self\-exe\" 

copy "out\static_content\-self\-test\*" "out\Windows\-self\-test\" 
copy "out\static_content\-self\-test\*" "out\Linux\-self\-test\" 

copy "Tests.cscs\cli.cs" "out\Linux\-self\-test\cli.cs" 
copy "Tests.cscs\cli.cs" "out\Windows\-self\-test\cli.cs" 

copy "out\static_content\readme.md" "out\Linux\readme.md" 

cd out\Windows
echo =====================
echo Aggregating packages (cd: %cd%)
echo =====================
css CSScriptLib\src\CSScriptLib\output\aggregate.cs
cd ..\..

copy CSScriptLib\src\CSScriptLib\output\*.*nupkg out\

echo =====================
echo Packaging (cd: %cd%)
echo =====================

cd out\Linux
echo cd: %cd%
..\ci\7z.exe a -r "..\cs-script.linux.7z" "*.*"
cd ..\..

cd out\Windows
echo cd: %cd%
..\ci\7z.exe a -r "..\cs-script.win.7z" "*.*"
cd ..\..

cd out\Windows
.\css -engine:csc -code var version = Assembly.LoadFrom(#''cscs.dll#'').GetName().Version.ToString();#nFile.Copy(#''..\\cs-script.win.7z#'', $#''..\\cs-script.win.v{version}.7z#'', true);
.\css -engine:csc -code var version = Assembly.LoadFrom(#''cscs.dll#'').GetName().Version.ToString();#nFile.Copy(#''..\\cs-script.linux.7z#'', $#''..\\cs-script.linux.v{version}.7z#'', true);
cd ..\..

del out\cs-script.win.7z
del out\cs-script.linux.7z


rem echo Published: %cd%
rem cd ..\..\.
:exit 
pause