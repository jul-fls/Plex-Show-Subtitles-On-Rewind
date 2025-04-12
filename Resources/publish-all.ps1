# PowerShell script to run all publishing profiles for a Visual Studio .NET project. Also automatically renames resulting binary file to include version number.
# Author: ThioJoe (https://github.com/ThioJoe)

# VERSION: 1.2.0 (Updated 4/4/25)

# --------------------------------------------------------------

# Tip 1: Make this script easier to run by adding it as an "External Tool" so it will show up in the "Tools" dropdown menu.
#
#     You can do this by:
#          1. Going to Tools > External Tools > Add
#          2. For 'Command' put 'powershell.exe'
#          3. For 'Arguments' use the -File parameter with the relative path (relative to project directory) and filename you have this script, for example:
#  	  		-File "Resources\publish-all.ps1"
#          4. Be sure to set the "Initial Directory" box to:
#            $(ProjectDir)
#
# Tip 2: You can pass built in variables in the Arguments box.
#
#     For example to set the file name base to the project target name:
#          -File "Resources\publish-all.ps1" -FileNameBase $(TargetName)
#
# --------------------------------------------------------------

param(
    [switch]$AddVersion,        # Switch: Adds the version number to the filename (defaulting to FileVersion, then AssemblyVersion, then Assembly Version Info)
	[switch]$AddRuntime,        # Switch to add FULL runtime ID + Type (e.g., _linux-x64-SelfContained)
	[switch]$AddArchitecture,   # Switch to add ONLY architecture (e.g., _x64)
    [string]$FileNameBase,      # String: Set the base name of the file instead of defaulting to Assembly Name
	[object]$AddType = $null,   # Switch to add deployment Type (e.g., -SelfContained). Also accepts "auto" to only add the type if there are multiple for the same runtime
    [string]$msbuildVerbose = "n"
)

# Helper function to get a property value from the first .csproj file found
function Get-CsProjProperty {
    param (
        [string]$PropertyName
    )

    # Find the first .csproj file in the current directory
    $csprojFile = Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1
    if (-not $csprojFile) {
        Write-Warning "No .csproj file found in the current directory."
        return $null
    }

    try {
        [xml]$csprojContent = Get-Content $csprojFile.FullName -Raw

        # --- Handle Potential XML Namespaces (Common in SDK-style projects) ---
        $namespaceURI = $csprojContent.DocumentElement.NamespaceURI
        $xpath = ""
        $node = $null

        if (-not [string]::IsNullOrWhiteSpace($namespaceURI)) {
            # If a default namespace exists, we MUST use a namespace manager
            $nsMgr = New-Object System.Xml.XmlNamespaceManager($csprojContent.NameTable)
            # Use a placeholder prefix (like 'msb') for the default namespace in XPath queries
            $nsMgr.AddNamespace("msb", $namespaceURI)
            # Construct XPath to find the property within any PropertyGroup, using the namespace prefix
            $xpath = "//msb:PropertyGroup/msb:$PropertyName"
            Write-Verbose "Using XML namespace '$namespaceURI' with prefix 'msb'. XPath: $xpath"
            # Select the first node matching the XPath query using the namespace manager
            $node = $csprojContent.SelectSingleNode($xpath, $nsMgr)
        } else {
            # If no namespace is defined (less common for modern projects)
            $xpath = "//PropertyGroup/$PropertyName"
             Write-Verbose "No XML namespace found. Using XPath: $xpath"
            # Select the first node matching the XPath query directly
            $node = $csprojContent.SelectSingleNode($xpath)
        }
        # --- End Namespace Handling ---

        # Check if a node was found
        if ($node) {
             $propertyValue = $node.'#text'.Trim() # Get the inner text and trim whitespace
             Write-Verbose "Found node for '$PropertyName' in $($csprojFile.Name). Value: '$propertyValue'"
             return $propertyValue
        } else {
            # Property wasn't found with the primary XPath query
            Write-Verbose "Property '$PropertyName' not found using XPath '$xpath' in $($csprojFile.Name)."
            # Optional: Could add fallback logic here if needed (e.g., check different paths), but often the above is sufficient if the property exists.
            return $null
        }
    } catch {
        Write-Warning "Error reading or parsing $($csprojFile.Name): $($_.Exception.Message)"
        return $null
    }
}

# Helper function to get the Target Framework Moniker (TFM)
function Get-TargetFramework {
    # Prefer TargetFrameworks (plural) if it exists, take the first one
    $targetFrameworks = Get-CsProjProperty -PropertyName "TargetFrameworks"
    if ($targetFrameworks) {
        $firstFramework = $targetFrameworks.Split(';')[0].Trim()
        Write-Verbose "Found TargetFrameworks, using first: $firstFramework"
        return $firstFramework
    }

    # Fallback to TargetFramework (singular)
    $targetFramework = Get-CsProjProperty -PropertyName "TargetFramework"
    if ($targetFramework) {
         Write-Verbose "Found TargetFramework: $targetFramework"
        return $targetFramework.Trim()
    }

    Write-Warning "Could not determine TargetFramework or TargetFrameworks from .csproj file."
    return $null # Indicate failure to find TFM
}

function Get-AssemblyVersion {
    param (
        [string]$configuration = "Release",
        [string]$runtime = ""
    )

    # --- Try to get version from .csproj file using helper ---
    $csprojVersion = Get-CsProjProperty -PropertyName "AssemblyVersion"
    if (-not [string]::IsNullOrEmpty($csprojVersion)) {
         if ($csprojVersion -match '^(\d+\.\d+\.\d+)\.(\d+)$') {
             $version = $matches[1]
             if ($matches[2] -ne "0") {
                 $version = "$version.$($matches[2])"
             }
             Write-Host "Found assembly version in .csproj file: $version"
             return $version
         } elseif ($csprojVersion -match '^(\d+\.\d+\.\d+)$') {
             # Handle 3-part version directly from csproj
             Write-Host "Found 3-part assembly version in .csproj file: $csprojVersion"
             return $csprojVersion
         } else {
              # Handle other formats if needed, or just return as is
              Write-Host "Found assembly version (unparsed) in .csproj file: $csprojVersion"
              return $csprojVersion
         }
    } else {
         # Optionally try FileVersion or Version properties as fallbacks from csproj
         $csprojFileVersion = Get-CsProjProperty -PropertyName "FileVersion"
         if (-not [string]::IsNullOrEmpty($csprojFileVersion)) {
             # Add parsing logic here if FileVersion format needs adjustment (e.g., matching AssemblyVersion format)
             if ($csprojFileVersion -match '^(\d+\.\d+\.\d+)\.(\d+)$') {
                 $version = $matches[1]
                 if ($matches[2] -ne "0") {
                     $version = "$version.$($matches[2])"
                 }
                 Write-Host "Found file version in .csproj file: $version"
                 return $version
             } elseif ($csprojFileVersion -match '^(\d+\.\d+\.\d+)$') {
                Write-Host "Found 3-part file version in .csproj file: $csprojFileVersion"
                return $csprojFileVersion
             } else {
                 Write-Host "Found file version (unparsed) in .csproj file: $csprojFileVersion"
                 return $csprojFileVersion
             }
         }
         # Add check for <Version> property if desired, applying similar parsing
         # $csprojPkgVersion = Get-CsProjProperty -PropertyName "Version" ...
    }

    Write-Host "Assembly/File Version not found directly in .csproj, checking generated AssemblyInfo.cs..."

    # --- Fallback to checking AssemblyInfo.cs ---

    # Get the TFM dynamically
    $targetFramework = Get-TargetFramework
    if (-not $targetFramework) {
        Write-Warning "Cannot determine Target Framework. Unable to find AssemblyInfo.cs path."
        Write-Warning "Returning default version 1.0.0"
        return "1.0.0" # Or handle error more severely if needed
    }

    # Construct the base path using the dynamic TFM
    $basePath = "obj\$configuration\$targetFramework"
    if ($runtime) {
        $basePath = Join-Path $basePath $runtime # Use Join-Path for robustness
    }

    # --- Attempt to find the AssemblyInfo.cs file using a pattern ---
    # Dynamically search for the generated file instead of using a hardcoded name
    $assemblyInfoSearchPath = Join-Path $basePath "*.AssemblyInfo.cs"
    Write-Verbose "Searching for AssemblyInfo file using pattern: $assemblyInfoSearchPath"

    # Find the first file matching the pattern in the target directory
    # Added -ErrorAction SilentlyContinue in case the path doesn't exist yet (e.g., before first build)
    $assemblyInfoFile = Get-ChildItem -Path $assemblyInfoSearchPath -File -ErrorAction SilentlyContinue | Select-Object -First 1

    # --- Check if the file was found and process it ---
    if ($assemblyInfoFile) {
        Write-Verbose "Found AssemblyInfo file: $($assemblyInfoFile.FullName)"
        # Use -Raw to read the whole file at once, potentially faster for smaller files
        $assemblyFileContent = Get-Content $assemblyInfoFile.FullName -Raw
		
		# First start with FileVersion. Ignore the last version digit if it's zero.
        $versionLine = $assemblyFileContent | Where-Object { $_ -match 'AssemblyFileVersionAttribute\("(.*)"\)' } | Select-Object -First 1
        if ($versionLine -match 'AssemblyFileVersionAttribute\("(\d+\.\d+\.\d+)\.(\d+)"\)') {
            $version = $matches[1]
            if ($matches[2] -ne "0") {
                $version = "$version.$($matches[2])"
            }
            Write-Host "Found assembly version in AssemblyFileVersionAttribute: $version"
            return $version
        }

        # Fall back to AssemblyVersionAttribute. Ignore the last version digit if it's zero.
        $versionLine = $assemblyFileContent | Where-Object { $_ -match 'AssemblyVersionAttribute\("(.*)"\)' } | Select-Object -First 1
        if ($versionLine -match 'AssemblyVersionAttribute\("(\d+\.\d+\.\d+)\.(\d+)"\)') {
            $version = $matches[1]
            if ($matches[2] -ne "0") {
                $version = "$version.$($matches[2])"
            }
            Write-Host "Found assembly version in AssemblyVersionAttribute: $version"
            return $version
        }

        # Fall back to AssemblyInformationalVersionAttribute
        $versionLine = $assemblyFileContent | Where-Object { $_ -match 'AssemblyInformationalVersionAttribute\("(.*)"\)' } | Select-Object -First 1
        # Updated regex to better handle versions like "1.2.3-beta+commitsha" -> extracts "1.2.3-beta"
        if ($versionLine -match 'AssemblyInformationalVersionAttribute\("([^"]+)"\)') {
             # Simpler extraction, might need refinement based on exact format needs
             $version = $matches[1]
             # Optional: remove build metadata if present (stuff after '+')
             $version = $version.Split('+')[0]
             Write-Host "Found assembly version in AssemblyInformationalVersionAttribute: $version"
             return $version
        }

        Write-Warning "Could not find recognized version attributes within $($assemblyInfoFile.Name)."

    } else {
        # File not found using the pattern
        Write-Warning "Couldn't find generated assembly info file (*.AssemblyInfo.cs) in expected path structure starting from: '$basePath'"
    }

    # Ultimate fallback if no version found anywhere
    Write-Warning "Couldn't find assembly version through .csproj or AssemblyInfo.cs. Using default 1.0.0"
    return "1.0.0"
}

# --- Main Script Logic ---

# Check if a .csproj file exists before proceeding
if (-not (Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1)) {
    Write-Error "No .csproj file found in the current directory. Script cannot continue."
    # Add Read-Host here if you want the error message to stay visible
    Read-Host "Press Enter to exit..."
    Exit 1 # Exit the script with an error code
}


# Create results directory
$resultsDir = "bin\Publish"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
Write-Host "Publishing results will be copied to: $($resultsDir)"

# --- Determine FileNameBase ---
# Check if the FileNameBase parameter was provided via command line and has a value
if (-not [string]::IsNullOrEmpty($FileNameBase)) {
    # If provided via parameter, the variable $FileNameBase is already set by PowerShell. Just log it.
    Write-Host "Using FileNameBase provided via parameter: $FileNameBase"
} else {
    # Parameter was NOT provided or was empty, determine dynamically from csproj AssemblyName or default
    Write-Verbose "FileNameBase parameter not provided or empty. Attempting to determine from .csproj..."
    $fileNameBase = Get-CsProjProperty -PropertyName "AssemblyName" # This assigns to the $fileNameBase variable only if the parameter wasn't used
    if ([string]::IsNullOrEmpty($fileNameBase)) {
        # Fallback: try to get it from the .csproj filename
        $csprojFile = Get-ChildItem -Filter "*.csproj" -File | Select-Object -First 1
        if ($csprojFile) {
            $fileNameBase = $csprojFile.BaseName # Assign the fallback value
            Write-Warning "AssemblyName not found in .csproj. Using .csproj filename as base: $fileNameBase"
        } else {
            # Ultimate fallback if somehow csproj disappeared after initial check
            $fileNameBase = "ProjectNameUnknown" # Assign the ultimate fallback value
            Write-Warning "Cannot determine AssemblyName or .csproj filename. Using default base: $fileNameBase"
        }
    } else {
         Write-Host "Using AssemblyName from .csproj: $fileNameBase"
    }
}
# --- End FileNameBase determination ---


# Get all publish profiles
$pubxmlFiles = Get-ChildItem -Path "Properties\PublishProfiles\*.pubxml" -File

if ($pubxmlFiles.Count -eq 0) {
    Write-Warning "No .pubxml files found in Properties\PublishProfiles\"
}

# --- Pre-process profiles to count runtimes (for AddType="auto") ---
$runtimeCounts = @{}
if ($AddType -is [string] -and $AddType -eq 'auto') {
    Write-Verbose "AddType='auto' detected. Pre-calculating runtime counts."
    foreach ($file in $pubxmlFiles) {
        try {
            [xml]$xmlContent = Get-Content $file.FullName
            # Check if PropertyGroup and RuntimeIdentifier exist before accessing
            $profileRuntimeNode = $xmlContent.SelectSingleNode("/Project/PropertyGroup/RuntimeIdentifier")
            if ($null -ne $profileRuntimeNode) {
                $profileRuntime = $profileRuntimeNode.'#text'.Trim()
                if (-not [string]::IsNullOrEmpty($profileRuntime)) {
                    if ($runtimeCounts.ContainsKey($profileRuntime)) {
                        $runtimeCounts[$profileRuntime]++
                    } else {
                        $runtimeCounts[$profileRuntime] = 1
                    }
                } else {
                     Write-Verbose "Profile '$($file.Name)' has an empty RuntimeIdentifier. Skipping for count."
                }
            } else {
                Write-Verbose "Skipping profile '$($file.Name)' for runtime count: RuntimeIdentifier node not found."
            }
        } catch {
            Write-Warning "Error processing '$($file.Name)' for runtime count: $($_.Exception.Message)"
        }
    }
    # You can uncomment the next line for debugging if needed
    # Write-Host "DEBUG: Runtime counts calculated: $($runtimeCounts | Out-String)"
}
# --- End Pre-processing ---

foreach ($pubxmlFile in $pubxmlFiles) {
    Write-Host "`n--------------------------------------------------"
    Write-Host "Processing publish profile: $($pubxmlFile.Name)"
    Write-Host "--------------------------------------------------"

    # Load and parse the pubxml file
    [xml]$pubxml = Get-Content $pubxmlFile.FullName
    $propertyGroup = $pubxml.Project.PropertyGroup

    # Extract properties from pubxml
    $publishDir = $propertyGroup.PublishDir
    $runtime = $propertyGroup.RuntimeIdentifier
    # Handle boolean conversion carefully, default to $false if missing/invalid
    $isSelfContained = $false
    if ($propertyGroup.SelfContained -ne $null -and $propertyGroup.SelfContained.ToLower() -eq 'true') {
         $isSelfContained = $true
    }

    # Validate essential properties
    if ([string]::IsNullOrEmpty($publishDir)) {
         Write-Warning "PublishDir not defined in $($pubxmlFile.Name). Skipping."
         continue
    }
     if ([string]::IsNullOrEmpty($runtime)) {
         Write-Warning "RuntimeIdentifier not defined in $($pubxmlFile.Name). Skipping."
         continue
    }


    # Determine architecture from runtime
    $architecture = if ($runtime -like "*-arm64*") { "ARM64" } elseif ($runtime -like "*-x64*") { "x64" } else { "x86" }

    # Determine type from SelfContained property
    $type = if ($isSelfContained) { "SelfContained" } else { "Framework" }

    # Get version for this specific configuration and runtime
    # Pass runtime only if it's relevant for finding AssemblyInfo.cs (which it is in our case)
    $version = Get-AssemblyVersion -configuration "Release" -runtime $runtime
    if ([string]::IsNullOrEmpty($version)) {
        Write-Warning "Failed to determine assembly version for profile $($pubxmlFile.Name). Skipping publish."
        continue # Skip this profile if version couldn't be found
    }

    # Construct the desired output assembly name based on parameters
	# --- Initialize filename components ---
	$versionPart = ""
	$runtimePart = ""
	$logVersion = "(without version)"
	$logRuntime = "(without runtime)"

	# --- Populate Version Component (if requested) ---
	if ($AddVersion) {
		# Note: $version should have been retrieved earlier, before the original block you're replacing.
		# If $version retrieval failed earlier, the script should have already skipped via 'continue'.
		if (-not [string]::IsNullOrEmpty($version)) {
			$safeVersion = $version -replace '[^a-zA-Z0-9\.\-]', '_'
			$versionPart = "_$safeVersion" # Set the version part including separator
			$logVersion = "(with version $version)"
		} else {
			 # This case should ideally not be reached if the earlier check worked, but as a safeguard:
			 Write-Warning "Version was requested but could not be determined for '$($pubxmlFile.Name)'. Version part will be missing."
		}
	}

	# --- Populate Runtime/Architecture Component (based on switches) ---
	$runtimeOrArchPart = "_$safeRuntime"               # Holds the string to append (e.g., "_linux-x64-SelfContained" or "_x64")
	$logRuntimeOrArch = "(with runtime $safeRuntime)" # Log message part

	if ($AddRuntime) { # Add Full Runtime + Type (takes precedence)
		# $runtime is the full ID from pubxml (e.g., "linux-x64", "osx-arm64", "win-x64")
		# $isSelfContained is boolean from pubxml
		$type = if ($isSelfContained) { "SelfContained" } else { "Framework" }
		# Sanitize the runtime string if needed (hyphens are generally safe)
		$safeRuntime = $runtime # Or add: -replace '[^a-zA-Z0-9\-\.]', '_'
		if (-not [string]::IsNullOrEmpty($safeRuntime)) {
			 $runtimeOrArchPart = "_$safeRuntime"
			 $logRuntimeOrArch = "(with runtime $safeRuntime)"
		} else {
			 Write-Warning "Runtime info was requested (-AddRuntime) but runtime ID from profile '$($pubxmlFile.Name)' was empty. Runtime part will be missing."
		}

	} elseif ($AddArchitecture) { # Add Architecture ONLY (if -AddRuntime wasn't used)
		 # $runtime is the full ID from pubxml
		 $architecture = if ($runtime -like "*-arm64*") { "ARM64" } elseif ($runtime -like "*-x64*") { "x64" } elseif ($runtime -like "*-x86*") { "x86" } else { $null } # Added x86 explicitly and default null
		 if (-not [string]::IsNullOrEmpty($architecture)) {
			$runtimeOrArchPart = "_$architecture"
			$logRuntimeOrArch = "(with architecture $architecture)"
		 } else {
			Write-Warning "Architecture info was requested (-AddArchitecture) but could not be determined from runtime ID '$runtime' in profile '$($pubxmlFile.Name)'. Architecture part will be missing."
		 }
	}
	
	# --- Populate Type Component (if requested based on $AddType mode) ---
	$typePart = ""
	$logType = "(without type info)"
	$addTypeFlag = $false # Flag to indicate if type should be added for this profile

	# Determine the type string based on SelfContained property (needed for all modes)
	# $isSelfContained is derived earlier in the loop from the pubxml
	$type = if ($isSelfContained) { "SelfContained" } else { "Framework" }

	# Check the state of the $AddType parameter
	if ($AddType -is [System.Management.Automation.SwitchParameter] -and $AddType.IsPresent) {
		# Mode 1: -AddType used as a switch - always add type
		$addTypeFlag = $true
		Write-Verbose "AddType switch detected. Adding type '$type'."

	} elseif ($AddType -is [string] -and $AddType -eq 'auto') {
		# Mode 2: -AddType "auto" - add type only if runtime is duplicated
		# $runtime is derived earlier in the loop from the pubxml
		Write-Verbose "AddType='auto' detected. Checking runtime '$runtime' count."
		if ($runtimeCounts.ContainsKey($runtime) -and $runtimeCounts[$runtime] -gt 1) {
			$addTypeFlag = $true
			Write-Verbose "Runtime '$runtime' has count $($runtimeCounts[$runtime]) (>1). Adding type '$type'."
		} else {
			$countInfo = if ($runtimeCounts.ContainsKey($runtime)) { $runtimeCounts[$runtime] } else { 0 }
			Write-Verbose "Runtime '$runtime' count is $countInfo (not >1). Not adding type."
		}

	} elseif ($AddType -is [string] -and -not [string]::IsNullOrEmpty($AddType) -and $AddType -ne 'auto') {
		# Handle invalid string value for AddType
		Write-Warning "Invalid string value '$AddType' provided for -AddType parameter. Ignoring type addition. Use '-AddType' (switch) or '-AddType auto'."
	}
	# If $AddType is $null (not provided), $addTypeFlag remains $false

	# Add the type part if the flag is set
	if ($addTypeFlag) {
		$typePart = "-$type" # Use hyphen as separator based on previous structure
		$logType = "(with type $type)"
	}
	# --- End Type Component Population ---

	# If neither switch is present, $runtimeOrArchPart remains ""

	# --- Assemble Final Name and Log Message (Single Point) ---
	# $versionPart should have been calculated based on $AddVersion before this block
	$assemblyName = $fileNameBase + $versionPart + $runtimeOrArchPart + $typePart

	# Construct log message using the pre-determined parts
	# $logVersion should be set based on $AddVersion before this block
	$publishLogMessage = "Publishing '$($pubxmlFile.BaseName)' $logVersion $logRuntimeOrArch $logType | Profile Runtime: $runtime | Output Assembly: $assemblyName"

	Write-Host $publishLogMessage
	
    $logDirectory = Join-Path -Path $resultsDir -ChildPath "logs"
    $logFileName = "$($pubxmlFile.BaseName)_publish_log.txt"
    $logFilePath = Join-Path -Path $logDirectory -ChildPath $logFileName
    # Creates intermediate directories if needed.
    New-Item -Path $logDirectory -ItemType Directory -Force | Out-Null

	# Add /p:UseAppHost=true to ensure an .exe is generated, especially for framework-dependent console apps if needed. Often default for WinExe.
    # Consider adding /p:PublishSingleFile=true or /p:PublishTrimmed=true if desired based on profiles
    dotnet publish /p:PublishProfile=$($pubxmlFile.BaseName) /p:Configuration=Release -v $($msbuildVerbose) *> $logFilePath # Ensure Release config is passed

    # --- Find, Rename (if needed), and Copy Published Output ---

	# 1. Determine the ORIGINAL base name (likely the $FileNameBase before suffixes)
	#    We'll use the $FileNameBase determined earlier in the script.
	$originalBaseName = $FileNameBase
	# $assemblyName variable already holds the desired FINAL name with suffixes

	# 2. Define potential ORIGINAL output file paths within the $publishDir
	$originalExe = Join-Path $publishDir "$originalBaseName.exe"    # Windows executable
	$originalNoExt = Join-Path $publishDir $originalBaseName        # Linux/macOS executable (no extension)
	$originalDll = Join-Path $publishDir "$originalBaseName.dll"    # The managed DLL

	# 3. Find which original primary executable/binary exists
	$originalPrimaryFile = $null
	$originalPrimaryFileName = $null # Just the filename part
	if (Test-Path $originalExe) {
		$originalPrimaryFile = $originalExe
		$originalPrimaryFileName = "$originalBaseName.exe"
	} elseif (Test-Path $originalNoExt) {
		$originalPrimaryFile = $originalNoExt
		$originalPrimaryFileName = $originalBaseName
	}
	# Note: We don't treat the DLL as the primary file to rename/copy here

	# 4. Rename the primary file (if found) to the desired $assemblyName format
	$renamedPrimaryFile = $null
	$renamedPrimaryFileName = $null

	if ($originalPrimaryFile) {
		# Construct the new name based on the original extension (or lack thereof)
		$newPrimaryFileName = if ($originalPrimaryFileName.EndsWith(".exe")) { "$assemblyName.exe" } else { $assemblyName }
		$newPrimaryFilePath = Join-Path $publishDir $newPrimaryFileName

		$destinationPath = Join-Path $resultsDir $newPrimaryFileName

		try {
			# Copy the original file directly to the results directory with the new name
			Copy-Item -Path $originalPrimaryFile -Destination $destinationPath -Force -ErrorAction Stop
			Write-Host "Successfully published and copied '$originalPrimaryFileName' to '$destinationPath'"

		} catch {
			Write-Error "Error copying '$originalPrimaryFileName' from '$originalPrimaryFile' to '$destinationPath': $($_.Exception.Message)"
			# Try the rest of the profiles
			continue
		}

		# --- OPTIONAL: Rename the accompanying .DLL if it exists ---
		# This is often NOT needed for self-contained apps after renaming the host,
		# but might be desired for consistency or required for framework-dependent apps
		# if you intended to rename both. Test carefully if you enable this.
		# if (Test-Path $originalDll) {
		#     $newDllFileName = "$assemblyName.dll"
		#     try {
		#         Rename-Item -Path $originalDll -NewName $newDllFileName -ErrorAction Stop
		#         Write-Verbose "Successfully renamed '$($originalDll.Name)' to '$newDllFileName' in '$publishDir'"
		#     } catch {
		#         Write-Warning "Could not rename accompanying DLL '$($originalDll.Name)' to '$newDllFileName': $($_.Exception.Message)"
		#     }
		# }
		# --- End Optional DLL Rename ---

	} else {
		# Original primary executable/binary wasn't found after publish
		Write-Warning "Warning: Could not find expected original output '$originalBaseName.exe' or '$originalBaseName' (no extension) after publishing profile '$($pubxmlFile.Name)' to location: '$publishDir'"
		Write-Warning "Check the build output and ensure the project's AssemblyName is '$originalBaseName'."
		continue # Skip copying for this profile
	}
	# --- End Find, Rename, Copy Block ---
}

# List all files in the results directory
Write-Host "`n--------------------------------------------------"
Write-Host "Published outputs copied to '$resultsDir':"
$outputFiles = Get-ChildItem $resultsDir -File
if ($outputFiles) {
    $outputFiles | ForEach-Object {
        Write-Host " - $($_.Name)"
    }
} else {
    Write-Host " - No files found."
}
Write-Host "--------------------------------------------------"

# Keep the window open
Read-Host "Script finished. Press Enter to exit..."
# SIG # Begin signature block
# MII98gYJKoZIhvcNAQcCoII94zCCPd8CAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCBXEUQGBJtBjdMN
# AfZs3kHQkI8w2hdrX5P47hMwRB+GmKCCIrQwggXMMIIDtKADAgECAhBUmNLR1FsZ
# lUgTecgRwIeZMA0GCSqGSIb3DQEBDAUAMHcxCzAJBgNVBAYTAlVTMR4wHAYDVQQK
# ExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xSDBGBgNVBAMTP01pY3Jvc29mdCBJZGVu
# dGl0eSBWZXJpZmljYXRpb24gUm9vdCBDZXJ0aWZpY2F0ZSBBdXRob3JpdHkgMjAy
# MDAeFw0yMDA0MTYxODM2MTZaFw00NTA0MTYxODQ0NDBaMHcxCzAJBgNVBAYTAlVT
# MR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xSDBGBgNVBAMTP01pY3Jv
# c29mdCBJZGVudGl0eSBWZXJpZmljYXRpb24gUm9vdCBDZXJ0aWZpY2F0ZSBBdXRo
# b3JpdHkgMjAyMDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBALORKgeD
# Bmf9np3gx8C3pOZCBH8Ppttf+9Va10Wg+3cL8IDzpm1aTXlT2KCGhFdFIMeiVPvH
# or+Kx24186IVxC9O40qFlkkN/76Z2BT2vCcH7kKbK/ULkgbk/WkTZaiRcvKYhOuD
# PQ7k13ESSCHLDe32R0m3m/nJxxe2hE//uKya13NnSYXjhr03QNAlhtTetcJtYmrV
# qXi8LW9J+eVsFBT9FMfTZRY33stuvF4pjf1imxUs1gXmuYkyM6Nix9fWUmcIxC70
# ViueC4fM7Ke0pqrrBc0ZV6U6CwQnHJFnni1iLS8evtrAIMsEGcoz+4m+mOJyoHI1
# vnnhnINv5G0Xb5DzPQCGdTiO0OBJmrvb0/gwytVXiGhNctO/bX9x2P29Da6SZEi3
# W295JrXNm5UhhNHvDzI9e1eM80UHTHzgXhgONXaLbZ7LNnSrBfjgc10yVpRnlyUK
# xjU9lJfnwUSLgP3B+PR0GeUw9gb7IVc+BhyLaxWGJ0l7gpPKWeh1R+g/OPTHU3mg
# trTiXFHvvV84wRPmeAyVWi7FQFkozA8kwOy6CXcjmTimthzax7ogttc32H83rwjj
# O3HbbnMbfZlysOSGM1l0tRYAe1BtxoYT2v3EOYI9JACaYNq6lMAFUSw0rFCZE4e7
# swWAsk0wAly4JoNdtGNz764jlU9gKL431VulAgMBAAGjVDBSMA4GA1UdDwEB/wQE
# AwIBhjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBTIftJqhSobyhmYBAcnz1AQ
# T2ioojAQBgkrBgEEAYI3FQEEAwIBADANBgkqhkiG9w0BAQwFAAOCAgEAr2rd5hnn
# LZRDGU7L6VCVZKUDkQKL4jaAOxWiUsIWGbZqWl10QzD0m/9gdAmxIR6QFm3FJI9c
# Zohj9E/MffISTEAQiwGf2qnIrvKVG8+dBetJPnSgaFvlVixlHIJ+U9pW2UYXeZJF
# xBA2CFIpF8svpvJ+1Gkkih6PsHMNzBxKq7Kq7aeRYwFkIqgyuH4yKLNncy2RtNwx
# AQv3Rwqm8ddK7VZgxCwIo3tAsLx0J1KH1r6I3TeKiW5niB31yV2g/rarOoDXGpc8
# FzYiQR6sTdWD5jw4vU8w6VSp07YEwzJ2YbuwGMUrGLPAgNW3lbBeUU0i/OxYqujY
# lLSlLu2S3ucYfCFX3VVj979tzR/SpncocMfiWzpbCNJbTsgAlrPhgzavhgplXHT2
# 6ux6anSg8Evu75SjrFDyh+3XOjCDyft9V77l4/hByuVkrrOj7FjshZrM77nq81YY
# uVxzmq/FdxeDWds3GhhyVKVB0rYjdaNDmuV3fJZ5t0GNv+zcgKCf0Xd1WF81E+Al
# GmcLfc4l+gcK5GEh2NQc5QfGNpn0ltDGFf5Ozdeui53bFv0ExpK91IjmqaOqu/dk
# ODtfzAzQNb50GQOmxapMomE2gj4d8yu8l13bS3g7LfU772Aj6PXsCyM2la+YZr9T
# 03u4aUoqlmZpxJTG9F9urJh4iIAGXKKy7aIwggbuMIIE1qADAgECAhMzAANGdP7w
# MGR/uCsfAAAAA0Z0MA0GCSqGSIb3DQEBDAUAMFoxCzAJBgNVBAYTAlVTMR4wHAYD
# VQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xKzApBgNVBAMTIk1pY3Jvc29mdCBJ
# RCBWZXJpZmllZCBDUyBBT0MgQ0EgMDEwHhcNMjUwNDExMDg1ODU0WhcNMjUwNDE0
# MDg1ODU0WjBsMQswCQYDVQQGEwJVUzEQMA4GA1UECBMHV3lvbWluZzERMA8GA1UE
# BxMIU2hlcmlkYW4xGzAZBgNVBAoTElRoaW8gU29mdHdhcmUsIExMQzEbMBkGA1UE
# AxMSVGhpbyBTb2Z0d2FyZSwgTExDMIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIB
# igKCAYEAr1++H5fMVxNGPV28asLarb6i2HqNpaxBnNGOL2W5nIv9viBdSJwVMGk4
# pOfIDaXi/hX9DdJx0SVpAQcyt0Pnoo0jLhMyOCGoHM/FR8A4wYgUl0S+/8LGPvWL
# vKODlMc7GdR2XUCuklxZKZMF8W3Eo8yhQ4PlWhaHZG06pq/HciWLuazjSYD82K7Q
# N1FJgltGgNeMErNqRZTNgbEJ05CS1P7sXCCAbmZVnJzSV09xwkAhVdBoXFUsFxQQ
# JYy28Ro4oy9pOeLY20teD7MM7L3IPG7vdnQ47OLKwApBNd/YaNYXuEhOQ+Hui1qk
# Me4e0drGW34MxbvABIWJnHUbRUm7F33XNLxkSPQFyZ5NoCcUb88blILy1HGEpj3K
# lAR8IgDOGRdzCf12tWBdChS+8Om7Whyq1Z+6fw7axR/8ooWjx2FNu8otgHkH1Joz
# p5bwzmtn7+QjZHRi++nN3uE9c4JDIoScJCoyMQHXo3xO6BpD84Oo3WlEzdpC5d4w
# 6ZqZ9yLHAgMBAAGjggIZMIICFTAMBgNVHRMBAf8EAjAAMA4GA1UdDwEB/wQEAwIH
# gDA8BgNVHSUENTAzBgorBgEEAYI3YQEABggrBgEFBQcDAwYbKwYBBAGCN2GCyITf
# TKbD3mqByam9GIO77qlmMB0GA1UdDgQWBBT3ZsVqFfgwtBGuL6R2bGcoOWd2zDAf
# BgNVHSMEGDAWgBTog8Qz19yfDJx2mgqm1N+Hpl5Y7jBnBgNVHR8EYDBeMFygWqBY
# hlZodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NybC9NaWNyb3NvZnQl
# MjBJRCUyMFZlcmlmaWVkJTIwQ1MlMjBBT0MlMjBDQSUyMDAxLmNybDCBpQYIKwYB
# BQUHAQEEgZgwgZUwZAYIKwYBBQUHMAKGWGh0dHA6Ly93d3cubWljcm9zb2Z0LmNv
# bS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwSUQlMjBWZXJpZmllZCUyMENTJTIw
# QU9DJTIwQ0ElMjAwMS5jcnQwLQYIKwYBBQUHMAGGIWh0dHA6Ly9vbmVvY3NwLm1p
# Y3Jvc29mdC5jb20vb2NzcDBmBgNVHSAEXzBdMFEGDCsGAQQBgjdMg30BATBBMD8G
# CCsGAQUFBwIBFjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3Mv
# UmVwb3NpdG9yeS5odG0wCAYGZ4EMAQQBMA0GCSqGSIb3DQEBDAUAA4ICAQCMu+fz
# 049wRUfwOljhVPC1epJBW2r4zMsrlJPeACLAfEClAcl4RFT6URTezJS81SmCyatD
# uFzDN0k2unhFaotFVOZwEZCM/+gX2H6jV6rzA6Yi2SpGK7T5p50GP91Cmt8gKIpr
# VU4jPHUGP0v3jFUhGJ4A1ggnBwdsZ0rrtVHsZrTY9pIBwdPW9p+QAOF4ZURq2Ngw
# vEFIb6WisAGH4nwhgmLSI1dTAAEDljuLIo6F+SpquPOdzo7qwqymNZTYHJ7WGwSU
# 2lLyn16uJ8m2AEFW7x9GcJF4ElOQd6hY6T2R00Tg4Rrw0zuWUmRjT64kkcW+nD/q
# qUAiTVkgAR//kz28k+ah+MTM6kQVhpXQXRCOd9p1bt9bNtJnGOp5pcmWY0P7m8Nb
# 0d0nw/gs6gNBqQPujLCi+51CcBCxt+vO0kDn45k++vPjTC8Xu3DEjfks41O7OpgE
# BMHzs/DdbTtMl7jwZ5Yuf62DESvvdT5HETaZbI+D9TJ+WC0Ewu6qx9DD20n88h/j
# cv4shBe9Nw/VhIU9bY3IwDtZqRiTaS70Bv7Vd6crFQauQejiC0GUaG73OulfLVYl
# CVapsHqnDGxvvLvTi7yoRSE4kHJkc1lEttK5FGBhXCWRki2LyNZtzneAj9q1QrGk
# M1ei0+aERXDkzOph/cLy3aKDkB0J2xE/9nDwQjCCBu4wggTWoAMCAQICEzMAA0Z0
# /vAwZH+4Kx8AAAADRnQwDQYJKoZIhvcNAQEMBQAwWjELMAkGA1UEBhMCVVMxHjAc
# BgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjErMCkGA1UEAxMiTWljcm9zb2Z0
# IElEIFZlcmlmaWVkIENTIEFPQyBDQSAwMTAeFw0yNTA0MTEwODU4NTRaFw0yNTA0
# MTQwODU4NTRaMGwxCzAJBgNVBAYTAlVTMRAwDgYDVQQIEwdXeW9taW5nMREwDwYD
# VQQHEwhTaGVyaWRhbjEbMBkGA1UEChMSVGhpbyBTb2Z0d2FyZSwgTExDMRswGQYD
# VQQDExJUaGlvIFNvZnR3YXJlLCBMTEMwggGiMA0GCSqGSIb3DQEBAQUAA4IBjwAw
# ggGKAoIBgQCvX74fl8xXE0Y9XbxqwtqtvqLYeo2lrEGc0Y4vZbmci/2+IF1InBUw
# aTik58gNpeL+Ff0N0nHRJWkBBzK3Q+eijSMuEzI4Iagcz8VHwDjBiBSXRL7/wsY+
# 9Yu8o4OUxzsZ1HZdQK6SXFkpkwXxbcSjzKFDg+VaFodkbTqmr8dyJYu5rONJgPzY
# rtA3UUmCW0aA14wSs2pFlM2BsQnTkJLU/uxcIIBuZlWcnNJXT3HCQCFV0GhcVSwX
# FBAljLbxGjijL2k54tjbS14Pswzsvcg8bu92dDjs4srACkE139ho1he4SE5D4e6L
# WqQx7h7R2sZbfgzFu8AEhYmcdRtFSbsXfdc0vGRI9AXJnk2gJxRvzxuUgvLUcYSm
# PcqUBHwiAM4ZF3MJ/Xa1YF0KFL7w6btaHKrVn7p/DtrFH/yihaPHYU27yi2AeQfU
# mjOnlvDOa2fv5CNkdGL76c3e4T1zgkMihJwkKjIxAdejfE7oGkPzg6jdaUTN2kLl
# 3jDpmpn3IscCAwEAAaOCAhkwggIVMAwGA1UdEwEB/wQCMAAwDgYDVR0PAQH/BAQD
# AgeAMDwGA1UdJQQ1MDMGCisGAQQBgjdhAQAGCCsGAQUFBwMDBhsrBgEEAYI3YYLI
# hN9MpsPeaoHJqb0Yg7vuqWYwHQYDVR0OBBYEFPdmxWoV+DC0Ea4vpHZsZyg5Z3bM
# MB8GA1UdIwQYMBaAFOiDxDPX3J8MnHaaCqbU34emXljuMGcGA1UdHwRgMF4wXKBa
# oFiGVmh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29m
# dCUyMElEJTIwVmVyaWZpZWQlMjBDUyUyMEFPQyUyMENBJTIwMDEuY3JsMIGlBggr
# BgEFBQcBAQSBmDCBlTBkBggrBgEFBQcwAoZYaHR0cDovL3d3dy5taWNyb3NvZnQu
# Y29tL3BraW9wcy9jZXJ0cy9NaWNyb3NvZnQlMjBJRCUyMFZlcmlmaWVkJTIwQ1Ml
# MjBBT0MlMjBDQSUyMDAxLmNydDAtBggrBgEFBQcwAYYhaHR0cDovL29uZW9jc3Au
# bWljcm9zb2Z0LmNvbS9vY3NwMGYGA1UdIARfMF0wUQYMKwYBBAGCN0yDfQEBMEEw
# PwYIKwYBBQUHAgEWM2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvRG9j
# cy9SZXBvc2l0b3J5Lmh0bTAIBgZngQwBBAEwDQYJKoZIhvcNAQEMBQADggIBAIy7
# 5/PTj3BFR/A6WOFU8LV6kkFbavjMyyuUk94AIsB8QKUByXhEVPpRFN7MlLzVKYLJ
# q0O4XMM3STa6eEVqi0VU5nARkIz/6BfYfqNXqvMDpiLZKkYrtPmnnQY/3UKa3yAo
# imtVTiM8dQY/S/eMVSEYngDWCCcHB2xnSuu1UexmtNj2kgHB09b2n5AA4XhlRGrY
# 2DC8QUhvpaKwAYfifCGCYtIjV1MAAQOWO4sijoX5Kmq4853OjurCrKY1lNgcntYb
# BJTaUvKfXq4nybYAQVbvH0ZwkXgSU5B3qFjpPZHTRODhGvDTO5ZSZGNPriSRxb6c
# P+qpQCJNWSABH/+TPbyT5qH4xMzqRBWGldBdEI532nVu31s20mcY6nmlyZZjQ/ub
# w1vR3SfD+CzqA0GpA+6MsKL7nUJwELG3687SQOfjmT768+NMLxe7cMSN+SzjU7s6
# mAQEwfOz8N1tO0yXuPBnli5/rYMRK+91PkcRNplsj4P1Mn5YLQTC7qrH0MPbSfzy
# H+Ny/iyEF703D9WEhT1tjcjAO1mpGJNpLvQG/tV3pysVBq5B6OILQZRobvc66V8t
# ViUJVqmweqcMbG+8u9OLvKhFITiQcmRzWUS20rkUYGFcJZGSLYvI1m3Od4CP2rVC
# saQzV6LT5oRFcOTM6mH9wvLdooOQHQnbET/2cPBCMIIHWjCCBUKgAwIBAgITMwAA
# AAc3jFuh2VuM1AAAAAAABzANBgkqhkiG9w0BAQwFADBjMQswCQYDVQQGEwJVUzEe
# MBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMTQwMgYDVQQDEytNaWNyb3Nv
# ZnQgSUQgVmVyaWZpZWQgQ29kZSBTaWduaW5nIFBDQSAyMDIxMB4XDTIxMDQxMzE3
# MzE1NFoXDTI2MDQxMzE3MzE1NFowWjELMAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1p
# Y3Jvc29mdCBDb3Jwb3JhdGlvbjErMCkGA1UEAxMiTWljcm9zb2Z0IElEIFZlcmlm
# aWVkIENTIEFPQyBDQSAwMTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIB
# ALf3wAMoB8W6Po8QNOITSPG8/hKm1r0rvXUYAGsIwmWrtWOeCEGpeQK6SVovHCI9
# FgPpqcIsfIk0YLqvSRC2b0zKNmdVOgLRNU595r6t5ssGfUnsVXfqzzk6/rnWuhlY
# MCwPNC4UX/NgL60sMZmaQh/5Wk9PHZ+/hrdHF2FTSiTkZtYUqwI4w0ImdSYqv0qo
# g6Bzejxpk+YVyrNthO/THhVmAeMtFJTaltGdizzbmvJNK2xEXYjUvp0z/8bBEmVJ
# 625sl2Wh73MhCmCvOVgc90q+hLfFZW3Ynf6YwaXByPh8raGlABhOq0lqpoe/2hGX
# gw1JM/bjCjhmrCIGJ3p1JORNG9lP/20Kg796nYY/VCWFgAzWAv94t4sXFtVRC2lP
# sE6l08SwBXpQov5UR0o2loqQ3hP02wW9M0woi53PZ5jo2TypbrTDYaPdcMaOIrXC
# aCJNmz6LfbfgFflMDWg0SG3BGkGgcnJfaXGMFpn9GyeCBZ/wpmDFRtXpAgcQcskT
# ZBZB5rjFy1+jzF3L3hOOozK/bMgtzPCR0XXUeM1Y3fxcQZ0uRzq6N6YOkrbhD7Ia
# 6eyPypNXExvMjlpfGqn+jiXgFchuJcifVfmcQpWJmlOcr7SmCpNRSwZxAXw1sPlA
# M0FdL3C6egFcdhXPJ9L6aMrna90sGZnJCbs+0fku/QczAgMBAAGjggIOMIICCjAO
# BgNVHQ8BAf8EBAMCAYYwEAYJKwYBBAGCNxUBBAMCAQAwHQYDVR0OBBYEFOiDxDPX
# 3J8MnHaaCqbU34emXljuMFQGA1UdIARNMEswSQYEVR0gADBBMD8GCCsGAQUFBwIB
# FjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3MvUmVwb3NpdG9y
# eS5odG0wGQYJKwYBBAGCNxQCBAweCgBTAHUAYgBDAEEwEgYDVR0TAQH/BAgwBgEB
# /wIBADAfBgNVHSMEGDAWgBTZQSmwDw9jbO9p1/XNKZ6kSGow5jBwBgNVHR8EaTBn
# MGWgY6Bhhl9odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NybC9NaWNy
# b3NvZnQlMjBJRCUyMFZlcmlmaWVkJTIwQ29kZSUyMFNpZ25pbmclMjBQQ0ElMjAy
# MDIxLmNybDCBrgYIKwYBBQUHAQEEgaEwgZ4wbQYIKwYBBQUHMAKGYWh0dHA6Ly93
# d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY2VydHMvTWljcm9zb2Z0JTIwSUQlMjBW
# ZXJpZmllZCUyMENvZGUlMjBTaWduaW5nJTIwUENBJTIwMjAyMS5jcnQwLQYIKwYB
# BQUHMAGGIWh0dHA6Ly9vbmVvY3NwLm1pY3Jvc29mdC5jb20vb2NzcDANBgkqhkiG
# 9w0BAQwFAAOCAgEAd/7rSyLZMC06eDX4ffb626UrC/DcDyI9vkwZ2Ri9wvHrUxmL
# Ph2VMTRvqzXmWvkc7vaBHAGSQ4hoxFsrczGrw+gYod5xFhbCFuGhoeIAeJ/6UT3n
# layuItSxu4yd5Dy0YTTh2Yi1ELZqxeFeiMbVBY7wZ4o2mfaCMbEkkD/IhzIz/dqY
# 0KxwRVhxg25JsfsjuiGp90Dd2Jm3X3sM0r50Jim5Ik4myYHCdS9E2ewcJrA75vtF
# 0bDznxYJ84S1amXRMlsMNdG35TngSqpG90mgjUE8/J7IWlGYa8VVOajYGI2mg0EJ
# vj9khKQRe0aJRAt5ZZX7i+ADhgraDXTzlq8fbwWU1wFXUxNq9CA5PPuctZCxkyys
# 5jRZ+pFQnOQXXTO8aA1kOGlKAkV3LAXkYbHJZO5FVz/syRjzl8I1CW589irfYhf/
# AehQzuG5Rrhq6IDKcl5LsAgtmeF3H8jIzw4amWADxhk1CDTLRW2ruUbRhtQzlhPX
# nQmobbjzA4Acnz4sb4ct7J+81B4CAv2LDkzMVbjEIv24GpnpqP1TSma7GWktJ85j
# sSqYPag3n4+XyfYCB13KdhdDh3FcHtypxF2vJKszA360Zf8MCqzUhZ96e4NG+OAH
# dTjQ5lretTC0LxeeCxutoun34UwnqBU/WfaW1KNoMZckxYOwZtH4DpZ0Xxgwggee
# MIIFhqADAgECAhMzAAAAB4ejNKN7pY4cAAAAAAAHMA0GCSqGSIb3DQEBDAUAMHcx
# CzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xSDBG
# BgNVBAMTP01pY3Jvc29mdCBJZGVudGl0eSBWZXJpZmljYXRpb24gUm9vdCBDZXJ0
# aWZpY2F0ZSBBdXRob3JpdHkgMjAyMDAeFw0yMTA0MDEyMDA1MjBaFw0zNjA0MDEy
# MDE1MjBaMGMxCzAJBgNVBAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9y
# YXRpb24xNDAyBgNVBAMTK01pY3Jvc29mdCBJRCBWZXJpZmllZCBDb2RlIFNpZ25p
# bmcgUENBIDIwMjEwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQCy8MCv
# GYgo4t1UekxJbGkIVQm0Uv96SvjB6yUo92cXdylN65Xy96q2YpWCiTas7QPTkGnK
# 9QMKDXB2ygS27EAIQZyAd+M8X+dmw6SDtzSZXyGkxP8a8Hi6EO9Zcwh5A+wOALNQ
# bNO+iLvpgOnEM7GGB/wm5dYnMEOguua1OFfTUITVMIK8faxkP/4fPdEPCXYyy8NJ
# 1fmskNhW5HduNqPZB/NkWbB9xxMqowAeWvPgHtpzyD3PLGVOmRO4ka0WcsEZqyg6
# efk3JiV/TEX39uNVGjgbODZhzspHvKFNU2K5MYfmHh4H1qObU4JKEjKGsqqA6Rzi
# ybPqhvE74fEp4n1tiY9/ootdU0vPxRp4BGjQFq28nzawuvaCqUUF2PWxh+o5/TRC
# b/cHhcYU8Mr8fTiS15kRmwFFzdVPZ3+JV3s5MulIf3II5FXeghlAH9CvicPhhP+V
# aSFW3Da/azROdEm5sv+EUwhBrzqtxoYyE2wmuHKws00x4GGIx7NTWznOm6x/niqV
# i7a/mxnnMvQq8EMse0vwX2CfqM7Le/smbRtsEeOtbnJBbtLfoAsC3TdAOnBbUkbU
# fG78VRclsE7YDDBUbgWt75lDk53yi7C3n0WkHFU4EZ83i83abd9nHWCqfnYa9qIH
# PqjOiuAgSOf4+FRcguEBXlD9mAInS7b6V0UaNwIDAQABo4ICNTCCAjEwDgYDVR0P
# AQH/BAQDAgGGMBAGCSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBTZQSmwDw9jbO9p
# 1/XNKZ6kSGow5jBUBgNVHSAETTBLMEkGBFUdIAAwQTA/BggrBgEFBQcCARYzaHR0
# cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9Eb2NzL1JlcG9zaXRvcnkuaHRt
# MBkGCSsGAQQBgjcUAgQMHgoAUwB1AGIAQwBBMA8GA1UdEwEB/wQFMAMBAf8wHwYD
# VR0jBBgwFoAUyH7SaoUqG8oZmAQHJ89QEE9oqKIwgYQGA1UdHwR9MHsweaB3oHWG
# c2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29mdCUy
# MElkZW50aXR5JTIwVmVyaWZpY2F0aW9uJTIwUm9vdCUyMENlcnRpZmljYXRlJTIw
# QXV0aG9yaXR5JTIwMjAyMC5jcmwwgcMGCCsGAQUFBwEBBIG2MIGzMIGBBggrBgEF
# BQcwAoZ1aHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9jZXJ0cy9NaWNy
# b3NvZnQlMjBJZGVudGl0eSUyMFZlcmlmaWNhdGlvbiUyMFJvb3QlMjBDZXJ0aWZp
# Y2F0ZSUyMEF1dGhvcml0eSUyMDIwMjAuY3J0MC0GCCsGAQUFBzABhiFodHRwOi8v
# b25lb2NzcC5taWNyb3NvZnQuY29tL29jc3AwDQYJKoZIhvcNAQEMBQADggIBAH8l
# Kp7+1Kvq3WYK21cjTLpebJDjW4ZbOX3HD5ZiG84vjsFXT0OB+eb+1TiJ55ns0BHl
# uC6itMI2vnwc5wDW1ywdCq3TAmx0KWy7xulAP179qX6VSBNQkRXzReFyjvF2BGt6
# FvKFR/imR4CEESMAG8hSkPYso+GjlngM8JPn/ROUrTaeU/BRu/1RFESFVgK2wMz7
# fU4VTd8NXwGZBe/mFPZG6tWwkdmA/jLbp0kNUX7elxu2+HtHo0QO5gdiKF+YTYd1
# BGrmNG8sTURvn09jAhIUJfYNotn7OlThtfQjXqe0qrimgY4Vpoq2MgDW9ESUi1o4
# pzC1zTgIGtdJ/IvY6nqa80jFOTg5qzAiRNdsUvzVkoYP7bi4wLCj+ks2GftUct+f
# GUxXMdBUv5sdr0qFPLPB0b8vq516slCfRwaktAxK1S40MCvFbbAXXpAZnU20FaAo
# Dwqq/jwzwd8Wo2J83r7O3onQbDO9TyDStgaBNlHzMMQgl95nHBYMelLEHkUnVVVT
# UsgC0Huj09duNfMaJ9ogxhPNThgq3i8w3DAGZ61AMeF0C1M+mU5eucj1Ijod5O2M
# MPeJQ3/vKBtqGZg4eTtUHt/BPjN74SsJsyHqAdXVS5c+ItyKWg3Eforhox9k3Wgt
# WTpgV4gkSiS4+A09roSdOI4vrRw+p+fL4WrxSK5nMYIalDCCGpACAQEwcTBaMQsw
# CQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSswKQYD
# VQQDEyJNaWNyb3NvZnQgSUQgVmVyaWZpZWQgQ1MgQU9DIENBIDAxAhMzAANGdP7w
# MGR/uCsfAAAAA0Z0MA0GCWCGSAFlAwQCAQUAoF4wEAYKKwYBBAGCNwIBDDECMAAw
# GQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwLwYJKoZIhvcNAQkEMSIEIFl2rfZh
# VkqG45ZEnVBrzCCRXzA7Arum2SBfXml36q/kMA0GCSqGSIb3DQEBAQUABIIBgDXp
# yvZSN90Kr20osd0Yvl57kZGaTJl8cU+m4yQ5QnygJWW2le7RzGENCP84iNlYcvSA
# 3ZJBOj+c2MkFKz/r/3dynKR7QOP41kuE27rsmWL3h9/CeLHNuFlHtgLckUFztM49
# GMIEc47rqyz0XH5FdS0JOMstZ87ARd5FtxqHHodqMrts3i6Yw/fjAl/IW18rzyT2
# BlDN6YuyzbaIS44CXentnuL/X1JJlfzgitBc7imMv0qU6hfGgzDuFZX6PKFmyUYZ
# FEtwDPTwDoYzaE0sc/4gMiRdLJy21O58o+NosIXMmFXzkRIuj520e4hS1FBZMHs4
# Scz50rFfLgXIGvIhd/MNT2HMNe979x5KJx2XqLoQO8wzEPzj5WrCJOHqQyeCKDx4
# oW2U7jskuFHWCUpTIHszMCjEa+ZsE1W9Rn/oLAjedfd/NgbcIbIgCS+B+32YMTXD
# lKvTefuYIKyST2HLREyJ/2SkUDdf3JjZ0696b8R9gbin0d4Ni9i9+WmM6MDsYKGC
# GBQwghgQBgorBgEEAYI3AwMBMYIYADCCF/wGCSqGSIb3DQEHAqCCF+0wghfpAgED
# MQ8wDQYJYIZIAWUDBAIBBQAwggFiBgsqhkiG9w0BCRABBKCCAVEEggFNMIIBSQIB
# AQYKKwYBBAGEWQoDATAxMA0GCWCGSAFlAwQCAQUABCDrB8yRuLIPvyC9e3ZXe6ic
# Q1VPWZ84lq0SRRM6UGq9+AIGZ+egyZnBGBMyMDI1MDQxMjA0NDY1Ni4yMjlaMASA
# AgH0oIHhpIHeMIHbMQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQ
# MA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
# MSUwIwYDVQQLExxNaWNyb3NvZnQgQW1lcmljYSBPcGVyYXRpb25zMScwJQYDVQQL
# Ex5uU2hpZWxkIFRTUyBFU046QTUwMC0wNUUwLUQ5NDcxNTAzBgNVBAMTLE1pY3Jv
# c29mdCBQdWJsaWMgUlNBIFRpbWUgU3RhbXBpbmcgQXV0aG9yaXR5oIIPITCCB4Iw
# ggVqoAMCAQICEzMAAAAF5c8P/2YuyYcAAAAAAAUwDQYJKoZIhvcNAQEMBQAwdzEL
# MAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjFIMEYG
# A1UEAxM/TWljcm9zb2Z0IElkZW50aXR5IFZlcmlmaWNhdGlvbiBSb290IENlcnRp
# ZmljYXRlIEF1dGhvcml0eSAyMDIwMB4XDTIwMTExOTIwMzIzMVoXDTM1MTExOTIw
# NDIzMVowYTELMAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3Jh
# dGlvbjEyMDAGA1UEAxMpTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5n
# IENBIDIwMjAwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoICAQCefOdSY/3g
# xZ8FfWO1BiKjHB7X55cz0RMFvWVGR3eRwV1wb3+yq0OXDEqhUhxqoNv6iYWKjkMc
# LhEFxvJAeNcLAyT+XdM5i2CgGPGcb95WJLiw7HzLiBKrxmDj1EQB/mG5eEiRBEp7
# dDGzxKCnTYocDOcRr9KxqHydajmEkzXHOeRGwU+7qt8Md5l4bVZrXAhK+WSk5Cih
# NQsWbzT1nRliVDwunuLkX1hyIWXIArCfrKM3+RHh+Sq5RZ8aYyik2r8HxT+l2hmR
# llBvE2Wok6IEaAJanHr24qoqFM9WLeBUSudz+qL51HwDYyIDPSQ3SeHtKog0ZubD
# k4hELQSxnfVYXdTGncaBnB60QrEuazvcob9n4yR65pUNBCF5qeA4QwYnilBkfnme
# AjRN3LVuLr0g0FXkqfYdUmj1fFFhH8k8YBozrEaXnsSL3kdTD01X+4LfIWOuFzTz
# uoslBrBILfHNj8RfOxPgjuwNvE6YzauXi4orp4Sm6tF245DaFOSYbWFK5ZgG6cUY
# 2/bUq3g3bQAqZt65KcaewEJ3ZyNEobv35Nf6xN6FrA6jF9447+NHvCjeWLCQZ3M8
# lgeCcnnhTFtyQX3XgCoc6IRXvFOcPVrr3D9RPHCMS6Ckg8wggTrtIVnY8yjbvGOU
# sAdZbeXUIQAWMs0d3cRDv09SvwVRd61evQIDAQABo4ICGzCCAhcwDgYDVR0PAQH/
# BAQDAgGGMBAGCSsGAQQBgjcVAQQDAgEAMB0GA1UdDgQWBBRraSg6NS9IY0DPe9iv
# Sek+2T3bITBUBgNVHSAETTBLMEkGBFUdIAAwQTA/BggrBgEFBQcCARYzaHR0cDov
# L3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9Eb2NzL1JlcG9zaXRvcnkuaHRtMBMG
# A1UdJQQMMAoGCCsGAQUFBwMIMBkGCSsGAQQBgjcUAgQMHgoAUwB1AGIAQwBBMA8G
# A1UdEwEB/wQFMAMBAf8wHwYDVR0jBBgwFoAUyH7SaoUqG8oZmAQHJ89QEE9oqKIw
# gYQGA1UdHwR9MHsweaB3oHWGc2h0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lv
# cHMvY3JsL01pY3Jvc29mdCUyMElkZW50aXR5JTIwVmVyaWZpY2F0aW9uJTIwUm9v
# dCUyMENlcnRpZmljYXRlJTIwQXV0aG9yaXR5JTIwMjAyMC5jcmwwgZQGCCsGAQUF
# BwEBBIGHMIGEMIGBBggrBgEFBQcwAoZ1aHR0cDovL3d3dy5taWNyb3NvZnQuY29t
# L3BraW9wcy9jZXJ0cy9NaWNyb3NvZnQlMjBJZGVudGl0eSUyMFZlcmlmaWNhdGlv
# biUyMFJvb3QlMjBDZXJ0aWZpY2F0ZSUyMEF1dGhvcml0eSUyMDIwMjAuY3J0MA0G
# CSqGSIb3DQEBDAUAA4ICAQBfiHbHfm21WhV150x4aPpO4dhEmSUVpbixNDmv6Tvu
# IHv1xIs174bNGO/ilWMm+Jx5boAXrJxagRhHQtiFprSjMktTliL4sKZyt2i+SXnc
# M23gRezzsoOiBhv14YSd1Klnlkzvgs29XNjT+c8hIfPRe9rvVCMPiH7zPZcw5nNj
# thDQ+zD563I1nUJ6y59TbXWsuyUsqw7wXZoGzZwijWT5oc6GvD3HDokJY401uhnj
# 3ubBhbkR83RbfMvmzdp3he2bvIUztSOuFzRqrLfEvsPkVHYnvH1wtYyrt5vShiKh
# eGpXa2AWpsod4OJyT4/y0dggWi8g/tgbhmQlZqDUf3UqUQsZaLdIu/XSjgoZqDja
# mzCPJtOLi2hBwL+KsCh0Nbwc21f5xvPSwym0Ukr4o5sCcMUcSy6TEP7uMV8RX0eH
# /4JLEpGyae6Ki8JYg5v4fsNGif1OXHJ2IWG+7zyjTDfkmQ1snFOTgyEX8qBpefQb
# F0fx6URrYiarjmBprwP6ZObwtZXJ23jK3Fg/9uqM3j0P01nzVygTppBabzxPAh/h
# Hhhls6kwo3QLJ6No803jUsZcd4JQxiYHHc+Q/wAMcPUnYKv/q2O444LO1+n6j01z
# 5mggCSlRwD9faBIySAcA9S8h22hIAcRQqIGEjolCK9F6nK9ZyX4lhthsGHumaABd
# WzCCB5cwggV/oAMCAQICEzMAAABIVXdyHnSSt/cAAAAAAEgwDQYJKoZIhvcNAQEM
# BQAwYTELMAkGA1UEBhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlv
# bjEyMDAGA1UEAxMpTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5nIENB
# IDIwMjAwHhcNMjQxMTI2MTg0ODUyWhcNMjUxMTE5MTg0ODUyWjCB2zELMAkGA1UE
# BhMCVVMxEzARBgNVBAgTCldhc2hpbmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAc
# BgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjElMCMGA1UECxMcTWljcm9zb2Z0
# IEFtZXJpY2EgT3BlcmF0aW9uczEnMCUGA1UECxMeblNoaWVsZCBUU1MgRVNOOkE1
# MDAtMDVFMC1EOTQ3MTUwMwYDVQQDEyxNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1l
# IFN0YW1waW5nIEF1dGhvcml0eTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoC
# ggIBAMt+gPdn75JVMhgkWcWc+tWUy9oliU9OuicMd7RW6IcA2cIpMiryomTjB5n5
# x/X68gntx2X7+DDcBpGABBP+INTq8s3pB8WDVgA7pxHu+ijbLhAMk+C4aMqka043
# EaP185q8CQNMfiBpMme4r2aG8jNSojtMQNXsmgrpLLSRixVxZunaYXhEngWWKoSb
# vRg1LAuOcqfmpghkmhBgqD1lZjNhpuCv1yUeyOVm0V6mxNifaGuKby9p4713KZ+T
# umZetBfY7zlRCXyToArYHwopBW402cFrfsQBZ/HGqU73tY6+TNug1lhYdYU6VLdq
# SW9Jr7vjY9JUjISCtoKCSogxmRW7MX7lCe7JV6Rdpn+HP7e6ObKvGyddRdtdiZCL
# p6dPtyiZYalN9GjZZm360TO+GXjpiZD0gZER+f5lEFavwIcD7HarW6qD0ZN81S+R
# DgfEtJ67h6oMUqP1WIiFC75if8gaK1aO5+Z8EqnaeKALgUVptF7i9KGsDvEm2ts4
# WYneMAhG2+7Z25+IjtW4ZAI83ZtdGOJp9sFd68S6EDf33wQLPi7CcZ9IUXW74tLv
# INktvw3PFee6I3hs/9fDcCMoEIav+WeZImILCgwRGFcLItvwpSEA7NcXToRk3TGf
# C53YD3g5NDujrqhduKLbVnorGOdIZXVeLMk0Jr4/XIUQGpUpAgMBAAGjggHLMIIB
# xzAdBgNVHQ4EFgQUpr139LrrfUoZ97y6Zho7Nzwc90cwHwYDVR0jBBgwFoAUa2ko
# OjUvSGNAz3vYr0npPtk92yEwbAYDVR0fBGUwYzBhoF+gXYZbaHR0cDovL3d3dy5t
# aWNyb3NvZnQuY29tL3BraW9wcy9jcmwvTWljcm9zb2Z0JTIwUHVibGljJTIwUlNB
# JTIwVGltZXN0YW1waW5nJTIwQ0ElMjAyMDIwLmNybDB5BggrBgEFBQcBAQRtMGsw
# aQYIKwYBBQUHMAKGXWh0dHA6Ly93d3cubWljcm9zb2Z0LmNvbS9wa2lvcHMvY2Vy
# dHMvTWljcm9zb2Z0JTIwUHVibGljJTIwUlNBJTIwVGltZXN0YW1waW5nJTIwQ0El
# MjAyMDIwLmNydDAMBgNVHRMBAf8EAjAAMBYGA1UdJQEB/wQMMAoGCCsGAQUFBwMI
# MA4GA1UdDwEB/wQEAwIHgDBmBgNVHSAEXzBdMFEGDCsGAQQBgjdMg30BATBBMD8G
# CCsGAQUFBwIBFjNodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3Mv
# UmVwb3NpdG9yeS5odG0wCAYGZ4EMAQQCMA0GCSqGSIb3DQEBDAUAA4ICAQBNrYvg
# HjRMA0wxiAI1dPL5y4pOMPM1nd0An5Lg9sAp/vwUHBDv2FiKGpn+oiDoZa3+NDkY
# CwFKkFgo1k4y1QwCs1B8iVnjbLa3KUA//EEZDrDCa7S4GZfODpbdOiZNnnpuH3SW
# Ltk7gFuKIKDYICSm+1O+uBi7sVu+9OpMi/8u9dBoInH6zG8k+xsgDJZRJ8hhN0Ba
# VWjrewnwCQfmnOmJ++QvJeYvGraNPLBp4P+kprMQnBcBvLz67TigIZUJkNsP6wM4
# nvneFuXpfJY5eYKldW+PbA+hcl0j5PoM+1z0Za0zFINQpm1UlXZRWAAJrPHyA4OJ
# 2PqHdobA6vxS38Ww79fzndDUJil8dZ9bckSQtzcWyUp/YqXbMfXgQGgt5SlPKSGf
# w1lR5eEey64qM/HyZQAtb8uCVSNlfInfIFDU+I56+nFOi3xp9dzquWr0UnaSC0zq
# KPa5bt/1q3nIhx3AUz1VSbRoKCJe+O9GRB5JQggCbjQtfaq97aR0+A179m3zJvnM
# NywmMeFk+1eJbdOcFRguoKwucPp9WHpflC8Vu2MuUEgy3deW8BCe5UTOGjK3eKzD
# D3Dy36gYKDho2H3gh0q9Q1LV9/EL5D5euxPfAOVKWo1It+ijGGwK7mBcq3Ol+HHz
# 7iX2tUcnGBkT2fAYqIBvA1fEoUHdtWCbCh0ltTGCB0YwggdCAgEBMHgwYTELMAkG
# A1UEBhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UE
# AxMpTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5nIENBIDIwMjACEzMA
# AABIVXdyHnSSt/cAAAAAAEgwDQYJYIZIAWUDBAIBBQCgggSfMBEGCyqGSIb3DQEJ
# EAIPMQIFADAaBgkqhkiG9w0BCQMxDQYLKoZIhvcNAQkQAQQwHAYJKoZIhvcNAQkF
# MQ8XDTI1MDQxMjA0NDY1NlowLwYJKoZIhvcNAQkEMSIEIDIGlf+c1G6SFHIy6mLr
# 2sl5SOdsBuS61d5sHam37VbiMIG5BgsqhkiG9w0BCRACLzGBqTCBpjCBozCBoAQg
# 6ioBV5tPCNafQ/SAvBnTdh+NfdC8O0dkSXfybyLzHUEwfDBlpGMwYTELMAkGA1UE
# BhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UEAxMp
# TWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5nIENBIDIwMjACEzMAAABI
# VXdyHnSSt/cAAAAAAEgwggNhBgsqhkiG9w0BCRACEjGCA1AwggNMoYIDSDCCA0Qw
# ggIsAgEBMIIBCaGB4aSB3jCB2zELMAkGA1UEBhMCVVMxEzARBgNVBAgTCldhc2hp
# bmd0b24xEDAOBgNVBAcTB1JlZG1vbmQxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jw
# b3JhdGlvbjElMCMGA1UECxMcTWljcm9zb2Z0IEFtZXJpY2EgT3BlcmF0aW9uczEn
# MCUGA1UECxMeblNoaWVsZCBUU1MgRVNOOkE1MDAtMDVFMC1EOTQ3MTUwMwYDVQQD
# EyxNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lIFN0YW1waW5nIEF1dGhvcml0eaIj
# CgEBMAcGBSsOAwIaAxUA5hJ9QZRXOOnEOHn3+omINFlowyegZzBlpGMwYTELMAkG
# A1UEBhMCVVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UE
# AxMpTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5nIENBIDIwMjAwDQYJ
# KoZIhvcNAQELBQACBQDro+tJMCIYDzIwMjUwNDExMTkyNjAxWhgPMjAyNTA0MTIx
# OTI2MDFaMHcwPQYKKwYBBAGEWQoEATEvMC0wCgIFAOuj60kCAQAwCgIBAAICBWQC
# Af8wBwIBAAICEiEwCgIFAOulPMkCAQAwNgYKKwYBBAGEWQoEAjEoMCYwDAYKKwYB
# BAGEWQoDAqAKMAgCAQACAwehIKEKMAgCAQACAwGGoDANBgkqhkiG9w0BAQsFAAOC
# AQEAerntbGKSq1fl/42gqDbDH5k+5BjcyyeJ4em/QNyv+fyjg2JT+cZvndMAGjUJ
# GvC6lcq1CTF/H4eKgNv9CXfcaiOZaN2P51+FS1PY6hLno+lidQO4mmMTy+9ELeMW
# GpTG15gwIlrcOmkgfhpyZaumOVyfoIXt/XIXLBpxzVrY6UCMNSc2M+LTYOCS/G5a
# /ulStDhfybAu241Y4YHtEYEO2g213VzOtKUXiO9V0iHgNMhtmasJgRoyhaPGjObA
# Fc+cL53/kquGjkjgB3nTTdLIFhYjmzMLULXmQzPYk/zWe+adbigxic46dcx55OdM
# USha6ES6iFPlsUOHg04JPX4gWDANBgkqhkiG9w0BAQEFAASCAgB++vZeYFfeeqMC
# eVwiOcZ+4tOV+cY+IlnGJV2WvZLrZyb09PW/uUYW0A9W619z1zXzdGIw+mXALDm+
# BE8amWwnjMjXJC7oGptYnjhESEiDv0gI/ImeKjkDp64aQTsAbIywNcg0nJN7bZXM
# 9CWd2KnsmAXCuzRP+RPbreRPSspQXwKiIWa+VEBJncsBpNjBxZBBRFa+BHOTE/OH
# 7BQzGyvQ1IZUVQXAgzxC8Ie8Vy27pxk8DPVi2l3OP+ascocJiojuapnrujMpeVMT
# y+/B4EyOEcUAlHNrRtasSJyQ/GLEXnZMv/XqftSzI7Xm2eGroo/njdNs03qf3WLp
# AztMg2pqQ44WEKKpnRjARP5JnKL5lGORTdD6yKR7qhm4YNmjb3D5NQKrbbBYomed
# FhGJhyeu2ynCqQhNOQB/7T45eI3usdZ1D8ftiK8sSyXWNAKQpC+tUvK47Rfy/3J8
# GNs3ljVqIGDFmkaDtGlGOc3ApMz9/r3WuwQmgwl00LcGYrW8oWCbSHaeeR5kdUDs
# auznSy4Ns6h8Lv0sdQ5XsTy5kwti9ZC+z8bdpeoaNPzqoD415YKdXnnK4jB/uGgK
# Zh7PTVaRoDQNWgKgmY+wvpIMnWAAkafZ1hDIUUjXMH/MzjvB6IU6s+xYOw7ZWQ2u
# BbNTPJFlHsAht5Ny5qS+7ZU3E6Xndw==
# SIG # End signature block
