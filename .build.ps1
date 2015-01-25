﻿param
(
)

$project_name = "HekadGateway"

# Folder structure:
# \build - Contains all code during the build process
# \build\artifacts - Contains all files during intermidiate bulid process
# \build\output - Contains the final result of the build process
# \release - Contains final release files for upload
# \release\archive - Contains files archived from the previous builds
# \src - Contains all source code
$build_dir = "$BuildRoot\build"
$build_artifacts_dir = "$build_dir\artifacts"
$build_output_dir = "$build_dir\output"
$release_dir = "$BuildRoot\release"
$archive_dir = "$release_dir\archive"

$src_dir = "$BuildRoot"
$solution_file = "$src_dir\$($project_name).sln"
	
# Use MSBuild.
use Framework\v4.0.30319 MSBuild

task Clean { 
	exec { MSBuild "$solution_file" /t:Clean /p:Configuration=Release /v:quiet } 
	Remove-Item -force -recurse $build_dir -ErrorAction SilentlyContinue | Out-Null
}

task Init Clean, { 
    New-Item $build_dir -itemType directory | Out-Null
    New-Item $build_artifacts_dir -itemType directory | Out-Null
    New-Item $build_output_dir -itemType directory | Out-Null
}

task Build {
	exec { MSBuild "$solution_file" /t:Build /p:Configuration=Release /v:minimal /p:OutDir="$build_artifacts_dir\" }
}

task Package  {
	New-Item $build_output_dir\$project_name\lib\net45 -itemType directory -force | Out-Null
	Copy-Item $build_artifacts_dir\$project_name.??? $build_output_dir\$project_name\lib\net45 -PassThru |% { Write-Host "Copied " $_.FullName }
}

# Set $script:Version = assembly version
task Version {
	assert (( Get-Item $build_artifacts_dir\$project_name.dll ).VersionInfo.FileVersion -match '^(\d+\.\d+\.\d+)')
	$script:Version = $matches[1]
}

task Archive {
	New-Item $release_dir -ItemType directory -Force | Out-Null
	New-Item $archive_dir -ItemType directory -Force | Out-Null
	Move-Item -Path $release_dir\*.* -Destination $archive_dir
}

task NuGet Package, Version, {

	Write-Host ================= Preparing $project_name Nuget package =================
	$text = "Assembly for pushing data to remote Hekad process."
	# nuspec
	Set-Content $build_output_dir\$project_name\$project_name.nuspec @"
<?xml version="1.0"?>
<package>
	<metadata>
		<id>$project_name</id>
		<version>$Version</version>
		<authors>Agile Harbor</authors>
		<owners>Agile Harbor</owners>
		<projectUrl>https://github.com/agileharbor/$project_name</projectUrl>
		<licenseUrl>https://raw.github.com/agileharbor/$project_name/master/License.txt</licenseUrl>
		<requireLicenseAcceptance>false</requireLicenseAcceptance>
		<copyright>Copyright (C) Agile Harbor, LLC</copyright>
		<summary>$text</summary>
		<description>$text</description>
		<tags>$project_short</tags>
		<dependencies> 
			<group targetFramework="net45">
				<dependency id="Newtonsoft.Json" version="5.0.8" />
				<dependency id="Serilog" version="1.4.102" />
				<dependency id="StatsdClient" version="1.0.0.16" />
			</group>
		</dependencies>
	</metadata>
</package>
"@
	# pack
	$nuget = "$($src_dir)\.nuget\NuGet"
	
	exec { & $nuget pack $build_output_dir\$project_name\$project_name.nuspec -Output $build_dir }
	
	$push_project = Read-Host "Push $($project_name) " $Version " to NuGet? (Y/N)"
	Write-Host $push_project
	if( $push_project -eq "y" -or $push_project -eq "Y" )	{
		Get-ChildItem $build_dir\*.nupkg |% { exec { & $nuget push  $_.FullName }}
	}
}

task . Init, Build, Package, NuGet