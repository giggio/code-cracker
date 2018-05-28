Properties {
    $rootDir = Split-Path $psake.build_script_file
    $configuration = "ReleaseNoVsix"
    $releaseDirName = "Release"
    $solutionFileCS = "$rootDir\CodeCracker.CSharp.sln"
    $solutionFileVB = "$rootDir\CodeCracker.VisualBasic.sln"
    $srcDir = "$rootDir\src"
    $testDir = "$rootDir\test"
    $isAppVeyor = $env:APPVEYOR -eq $true
    $slns = ls "$rootDir\*.sln"
    $packagesDir = "$rootDir\packages"
    $buildNumber = [Convert]::ToInt32($env:APPVEYOR_BUILD_NUMBER).ToString("0000")
    $nupkgPathCS = "$rootDir\src\CSharp\CodeCracker.CSharp.{0}.nupkg"
    $nupkgPathVB = "$rootDir\src\VisualBasic\CodeCracker.VisualBasic.{0}.nupkg"
    $xunitConsoleExe = "$packagesDir\xunit.runner.console.2.3.1\tools\net452\xunit.console.x86.exe"
    $openCoverExe = "$packagesDir\OpenCover.4.6.519\tools\OpenCover.Console.exe"
    $dllCS = "CodeCracker.CSharp.dll"
    $dllVB = "CodeCracker.VisualBasic.dll"
    $dllCommon = "CodeCracker.Common.dll"
    $testDllCS = "CodeCracker.Test.CSharp.dll"
    $testDllVB = "CodeCracker.Test.VisualBasic.dll"
    $testDirCS = "$testDir\CSharp\CodeCracker.Test\bin\$releaseDirName"
    $testDirVB = "$testDir\VisualBasic\CodeCracker.Test\bin\$releaseDirName"
    $projectDirVB = "$srcDir\VisualBasic\CodeCracker"
    $projectFileVB = "$projectDirVB\CodeCracker.vbproj"
    $releaseDirVB = "$projectDirVB\bin\$releaseDirName\netstandard1.3"
    $projectDirCS = "$srcDir\CSharp\CodeCracker"
    $projectFileCS = "$projectDirCS\CodeCracker.csproj"
    $releaseDirCS = "$projectDirCS\bin\$releaseDirName\netstandard1.3"
    $logDir = "$rootDir\log"
    $outputXml = "$logDir\CodeCoverageResults.xml"
    $reportGeneratorExe = "$packagesDir\ReportGenerator.3.1.2\tools\ReportGenerator.exe"
    $coverageReportDir = "$logDir\codecoverage\"
    $coverallsNetExe = "$packagesDir\coveralls.io.1.4.2\tools\coveralls.net.exe"
    $ilmergeExe = "$packagesDir\ilmerge.2.14.1208\tools\ILMerge.exe"
    $pdb2pdbExe = "$packagesDir\pdb2pdb.1.1.0-beta1-62810-01\tools\Pdb2Pdb.exe"
    $isRelease = $isAppVeyor -and (($env:APPVEYOR_REPO_BRANCH -eq "release") -or ($env:APPVEYOR_REPO_TAG -eq "true"))
    $isPullRequest = $env:APPVEYOR_PULL_REQUEST_NUMBER -ne $null
    $tempDir = Join-Path "$([System.IO.Path]::GetTempPath())" "CodeCracker"
    $appVeyorLogger = "C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll"
}

FormatTaskName (("-"*25) + "[{0}]" + ("-"*25))

Task Default -Depends Build, Test

Task Rebuild -Depends Clean, Build

Task Restore {
    Foreach($sln in $slns) {
        RestorePkgs $sln
    }
}

Task Prepare-Build -depends Restore

Task Build -depends Prepare-Build, Build-Only
Task Build-CS -depends Prepare-Build, Build-Only-CS
Task Build-VB -depends Prepare-Build, Build-Only-VB

Task Build-Only -depends Build-Only-CS, Build-Only-VB
Task Build-Only-CS -depends Build-DotNet-CS, ILMerge-CS
Task Build-DotNet-CS {
    if ($isAppVeyor) { # todo: use dotnet build with appveyour when done https://github.com/appveyor/ci/issues/2212
        if (Test-Path $appVeyorLogger) {
            Exec { dotnet msbuild $solutionFileCS /m /verbosity:minimal /p:VersionSuffix=z$buildNumber /p:Configuration=$configuration /logger:"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll" }
        } else {
            Write-Host "Appveyor logger not found."
            Exec { dotnet msbuild $solutionFileCS /m /verbosity:minimal /p:VersionSuffix=z$buildNumber /p:Configuration=$configuration }
        }
    } else {
        Exec { dotnet build $solutionFileCS --no-restore --verbosity minimal --configuration $configuration }
    }
}
Task Build-Only-VB -depends Build-DotNet-VB, ILMerge-VB
Task Build-DotNet-VB {
    if ($isAppVeyor) { # todo: use dotnet build with appveyour when done https://github.com/appveyor/ci/issues/2212
        if (Test-Path $appVeyorLogger) {
            Exec { dotnet msbuild $solutionFileVB /m /verbosity:minimal /p:VersionSuffix=z$buildNumber /p:Configuration=$configuration /logger:"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll" }
        } else {
            Write-Host "Appveyor logger not found."
            Exec { dotnet msbuild $solutionFileVB /m /verbosity:minimal /p:VersionSuffix=z$buildNumber /p:Configuration=$configuration /logger:"C:\Program Files\AppVeyor\BuildAgent\Appveyor.MSBuildLogger.dll" }
        }
    } else {
        Exec { dotnet build $solutionFileVB --verbosity minimal --configuration $configuration }
    }
}

Task ILMerge-VB { ILMerge $releaseDirVB $dllVB $projectFileVB $projectDirVB }
Task ILMerge-CS { ILMerge $releaseDirCS $dllCS $projectFileCS $projectDirCS }

function ConvertPdb($inputDll) {
    $extension = (ls "$inputDll").Extension
    $fullName = (ls "$inputDll").FullName
    $pdb = $fullName.Substring(0, $fullName.Length - $extension.Length) + ".pdb"
    Write-Host "Converting pdb '$pdb' for dll '$inputDll'"
    Exec { . $pdb2pdbExe "$inputDll" /pdb "$pdb" }
    mv -Force "$($pdb)2" "$pdb"
}

function GetDeps($projectDir) {
    Write-Host "Getting deps in '$projectDir'"
    # Write-Host dotnet msbuild /t:GetInputDlls /v:m /p:OutputPath=$tempDir
    Push-Location $projectDir
    $buildLog = dotnet msbuild /t:GetInputDlls /v:m /p:OutputPath=$tempDir /p:Configuration=$configuration | Out-String
    if ($LASTEXITCODE -ne 0) { throw "Build failed, build log: $buildLog" }
    # Write-Host Build log is $buildLog
    Pop-Location
    $dllDirs = $buildLog.Substring($buildLog.IndexOf("'") + 1, $buildLog.LastIndexOf("'") - $buildLog.IndexOf("'") - 1).Split(';') | % { [System.IO.Path]::GetDirectoryName("$_") } | Get-Unique | ? { Test-Path $_ }
    return $dllDirs
}

function ILMerge($releaseDir, $dll, $projectFile, $projectDir) {
    if (!(Test-Path $ilmergeExe)) {
        throw "IL Merge not found at '$ilmergeExe'"
    }
    Write-Host "IL Merge:"
    $mergedDir = $tempDir
    if (!(Test-Path $mergedDir)) { mkdir "$mergedDir" }
    $inputDll = "$releaseDir\$dll"
    ConvertPdb $inputDll
    $inputDllCommon = "$releaseDir\$dllCommon"
    $pdbCommon = Change-Extension $inputDllCommon "pdb"
    if (Test-Path $inputDllCommon) {
        if ((ls $inputDllCommon).LastWriteTime -gt (ls $inputDll).LastWriteTime) {
            # common is newer, but no changes on main dll
            Write-Host "Common dll is newer than $inputDll, stopping IL merge."
            return
        }
    } else {
        # no common dll, can't merge
        throw "Can't find common dll at '$inputDllCommon', stopping IL merge."
    }
    ConvertPdb $inputDllCommon
    $mergedDll = "$mergedDir\$dll"
    $libs = @()
    foreach ($dllDir in $(GetDeps $projectDir)) {
        $libs += "/lib:$dllDir "
    }
    # Write-Host "Running ILMerge with:"
    # Write-Host "    $ilmergeExe $libs /out:`"$mergedDll`" `"$inputDll`" `"$inputDllCommon`""
    Exec { . $ilmergeExe $libs /out:"$mergedDll" "$inputDll" "$inputDllCommon" }
    $releaseMergedDir = $releaseDir
    if (!(Test-Path $releaseMergedDir)) { mkdir $releaseMergedDir | Out-Null }
    cp $mergedDll "$releaseMergedDir\" -Force
    Write-Host "  $dll -> $releaseMergedDir\$dll"
    $mergedPdb = Change-Extension $mergedDll "pdb"
    cp $mergedPdb "$releaseMergedDir\" -Force
    $pdb = (ls $mergedPdb).Name
    Write-Host "  $pdb -> $releaseMergedDir\$pdb"
}

function Change-Extension ($filename, $extension) {
    Join-Path "$([System.IO.Path]::GetDirectoryName($filename))" "$([System.IO.Path]::GetFileNameWithoutExtension($filename)).$extension"
}

Task Clean {
    Exec { dotnet clean $solutionFileCS /p:Configuration=$configuration --verbosity quiet }
    Exec { dotnet clean $solutionFileVB /p:Configuration=$configuration --verbosity quiet }
}

Task Set-Log {
    if ((Test-Path $logDir) -eq $false)
    {
        Write-Host -ForegroundColor DarkBlue "Creating log directory $logDir"
        mkdir $logDir | Out-Null
    }
}

Task Test-Acceptance -depends Test {
    . "$rootDir\test\CSharp\AnalyzeCoreFx.ps1"
}

Task Test -depends Set-Log {
    RunTestWithCoverage "$testDirCS\$testDllCS", "$testDirVB\$testDllVB"
}
Task Test-VB -depends Set-Log {
    RunTestWithCoverage "$testDirVB\$testDllVB"
}
Task Test-CSharp -depends Set-Log {
    RunTestWithCoverage "$testDirCS\$testDllCS"
}

Task Test-No-Coverage -depends Test-No-Coverage-CSharp, Test-No-Coverage-VB
Task Test-No-Coverage-VB {
    RunTest "$testDirVB\$testDllVB"
}
Task Test-No-Coverage-CSharp {
    RunTest "$testDirCS\$testDllCS"
}

Task Pack-Nupkg -precondition { return $isAppVeyor } -depends Pack-Nupkg-Csharp, Pack-Nupkg-VB
Task Pack-Nupkg-CSharp -depends Pack-Nupkg-Csharp-Force -precondition { return $isAppVeyor }
Task Pack-Nupkg-VB -depends Pack-Nupkg-VB-Force -precondition { return $isAppVeyor }
Task Pack-Nupkg-Force -depends Pack-Nupkg-Csharp-Force, Pack-Nupkg-VB-Force
Task Pack-Nupkg-Csharp-Force {
    PackNupkg "C#" "$rootDir\src\CSharp" $projectFileCS $nupkgPathCS
}
Task Pack-Nupkg-VB-Force {
    PackNupkg "VB" "$rootDir\src\VisualBasic" $projectFileVB $nupkgPathVB
}

Task Count-Analyzers {
    $count = $(ls $rootDir\src\*.cs -Recurse | ? { $_.Name.contains('Analyzer') } | ? { !((cat $_) -match 'abstract class') }).count
    Write-Host "Found $count C# Analyzers"
    $count = $(ls $rootDir\src\*.cs -Recurse | ? { $_.Name.contains('CodeFix') } | ? { !((cat $_) -match 'abstract class') }).count
    Write-Host "Found $count C# Code Fixes"
    $count = $(ls $rootDir\src\*.cs -Recurse | ? { $_.Name.contains('FixAll') } | ? { !((cat $_) -match 'abstract class') }).count
    Write-Host "Found $count C# Code Fixes All"
    $count = $(ls $rootDir\src\*.vb -Recurse | ? { $_.Name.contains('Analyzer') } | ? { !((cat $_) -match 'mustinherit class') }).count
    Write-Host "Found $count VB Analyzers"
    $count = $(ls $rootDir\src\*.vb -Recurse | ? { $_.Name.contains('CodeFix') } | ? { !((cat $_) -match 'mustinherit class') }).count
    Write-Host "Found $count VB Code Fixes"
    $count = $(ls $rootDir\src\*.vb -Recurse | ? { $_.Name.contains('FixAll') } | ? { !((cat $_) -match 'mustinherit class') }).count
    Write-Host "Found $count VB Code Fixes All"
}

Task Update-ChangeLog {
    # invoke-psake default.ps1 -tasklist update-changelog -parameters @{"token"="<token>"}
    echo $token
    return
    Exec {
        github_changelog_generator code-cracker/code-cracker --no-pull-requests --no-issues-wo-labels --exclude-labels "Can't repro","update readme",decision,docs,duplicate,question,invalid,wontfix,Duplicate,Question,Invalid,Wontfix  -t $token
    }
}

Task Echo { echo echo }

function PackNupkg($language, $dir, $projPath, $nupkgFile) {
    Write-Host "Packing nupkg for $language with $projPath..."
    if ($isAppVeyor) {
        Exec { dotnet pack "$projPath" --configuration $releaseDirName --no-build --version-suffix z$buildNumber --output "$dir" }
    } else {
        Exec { dotnet pack "$projPath" --configuration $releaseDirName --no-build --output "$dir" }
    }
    $projFileName = (ls $projPath).Name
    [xml]$dirBuildProps = cat $rootDir\Directory.Build.props
    $version = $dirBuildProps.Project.PropertyGroup.VersionPrefix
    $nupkgFile = $nupkgFile -f $version
    Write-Host "  $projFileName ($language/$($version)) -> $nupkgFile"
    if ($isAppVeyor) {
        if (gcm appveyor -ErrorAction Ignore) {
            Write-Host "Pushing nupkg artifact for $language..."
            Exec { appveyor PushArtifact $nupkgFile }
            Write-Host "Nupkg pushed for $language!"
        } else {
            Write-Host "Could not find 'appveyor' command to push '$nupkgFile' for $language."
        }
    }
}

function RestorePkgs($sln) {
    Write-Host "Restoring $sln..." -ForegroundColor Green
    Retry {
        dotnet restore "$sln" --configfile "$rootDir\nuget.config" /p:RestoreUseSkipNonexistentTargets=false
        if ($LASTEXITCODE) { throw "dotnet restore for $sln failed." }
    }
}

function Retry {
     Param (
        [parameter(Position=0,Mandatory=1)]
        [ScriptBlock]$cmd,
        [parameter(Position=1,Mandatory=0)]
        [int]$times = 3
    )
    $retrycount = 0
    while ($retrycount -lt $times){
        try {
            & $cmd
            if (!$?) {
                throw "Command failed."
            }
            return
        }
        catch {
            Write-Host -ForegroundColor Red "Failed: ($($_.Exception.Message)), retrying."
        }
        $retrycount++
    }
    throw "Command '$($cmd.ToString())' failed."
}

function TestPath($paths) {
    $notFound = @()
    foreach($path in $paths) {
        if ((Test-Path $path) -eq $false)
        {
            $notFound += $path
        }
    }
    $notFound
}

function RunTest($fullTestDllPath) {
    if ($isAppVeyor) {
        . $xunitConsoleExe $fullTestDllPath -appveyor -nologo -quiet
    } else {
        . $xunitConsoleExe $fullTestDllPath -nologo -quiet
    }
}

function RunTestWithCoverage($fullTestDllPaths) {
    $notFoundPaths = TestPath $openCoverExe, $xunitConsoleExe, $reportGeneratorExe
    if ($notFoundPaths.length -ne 0) {
        Write-Host -ForegroundColor DarkRed "Paths not found: "
        foreach($path in $notFoundPaths) {
            Write-Host -ForegroundColor DarkRed "    $path"
        }
        throw "Paths for test executables not found"
    }
    $targetArgs = ""
    Foreach($fullTestDllPath in $fullTestDllPaths) {
        $targetArgs += $fullTestDllPath + " "
    }
    $targetArgs = $targetArgs.Substring(0, $targetArgs.Length - 1)
    $appVeyor = ""
    if ($isAppVeyor) {
        $appVeyor = " -appveyor"
    }
    $arguments = '-register:user', "`"-target:$xunitConsoleExe`"", "`"-targetargs:$targetArgs $appVeyor -noshadow -parallel none -nologo`"", "`"-filter:+[CodeCracker*]* -[CodeCracker.Test*]*`"", "`"-output:$outputXml`"", '-coverbytest:*.Test.*.dll', '-log:All', '-returntargetcode'
    Exec { . $openCoverExe $arguments }
    Write-Host -ForegroundColor DarkBlue "Exporting code coverage report"
    Exec { . $reportGeneratorExe -verbosity:Info -reports:$outputXml -targetdir:$coverageReportDir }
    if ($env:COVERALLS_REPO_TOKEN -ne $null) {
        Write-Host -ForegroundColor DarkBlue "Uploading coverage report to Coveralls.io"
        Exec { . $coverallsNetExe --opencover $outputXml --full-sources }
    }
}