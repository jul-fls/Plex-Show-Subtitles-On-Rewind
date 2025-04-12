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
	[object]$AddType = $null    # Switch to add deployment Type (e.g., -SelfContained). Also accepts "auto" to only add the type if there are multiple for the same runtime

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
	
	# Add /p:UseAppHost=true to ensure an .exe is generated, especially for framework-dependent console apps if needed. Often default for WinExe.
    # Consider adding /p:PublishSingleFile=true or /p:PublishTrimmed=true if desired based on profiles
    dotnet publish /p:PublishProfile=$($pubxmlFile.BaseName) /p:Configuration=Release # Ensure Release config is passed

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
# MII6fgYJKoZIhvcNAQcCoII6bzCCOmsCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCC0C5btjPIfdcbT
# 7knNrpzL0ofhHsiLAw5aa6gvdbF4Q6CCIrQwggXMMIIDtKADAgECAhBUmNLR1FsZ
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
# WTpgV4gkSiS4+A09roSdOI4vrRw+p+fL4WrxSK5nMYIXIDCCFxwCAQEwcTBaMQsw
# CQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSswKQYD
# VQQDEyJNaWNyb3NvZnQgSUQgVmVyaWZpZWQgQ1MgQU9DIENBIDAxAhMzAANGdP7w
# MGR/uCsfAAAAA0Z0MA0GCWCGSAFlAwQCAQUAoF4wEAYKKwYBBAGCNwIBDDECMAAw
# GQYJKoZIhvcNAQkDMQwGCisGAQQBgjcCAQQwLwYJKoZIhvcNAQkEMSIEIN+rRds1
# oE7fyqGsEmymPO0uFGmVymtdZ/2kNo7RULGtMA0GCSqGSIb3DQEBAQUABIIBgBKi
# WEB0dtEXZWootO43UP7nIwRLeIgjERNJb2/lY7gIeUpkm3OWMLZhG4L45FPNJKPO
# z91w5Fxmbr1iauPlp4mXdxIAIcF0Y301GcDqCB51WijG3p2aoup+nizbkEz0jsWx
# Y2q0CEVrRb1T5zw18EnXa2EfJZyXYdR/8x75w5eDYRaJbA2IarYSV6l7prVf1w7v
# 6vvZ5Pyqo0Lh+rfUQRghOff5K0trjnJgy1Vc1dTNL26G6h+zp2UlWnHw5glz2GQa
# 5abMVRIE+7xnbj6DeleyJ3a0QYfSCGsyXalwZDtsUxatyIFZZs55+lMeFE85wC2X
# Lf+OX2HuhJr9qeSFWTN9xIWZVvTnjTLw8PtjWOmVnqJJK0sy0orUfPQQCBkZHNCh
# fkYlf3mWpXUpKehs94eQSb03a8vNs15opJBLehVoRBxBirwkr/dcfDbnlBT+SUnd
# GjDKYDd+Ci+iUBzj/QVSt+anVL++6JFkC5yUV2CdB5AXMrh6572g9w9jPnQ8JKGC
# FKAwghScBgorBgEEAYI3AwMBMYIUjDCCFIgGCSqGSIb3DQEHAqCCFHkwghR1AgED
# MQ8wDQYJYIZIAWUDBAIBBQAwggFhBgsqhkiG9w0BCRABBKCCAVAEggFMMIIBSAIB
# AQYKKwYBBAGEWQoDATAxMA0GCWCGSAFlAwQCAQUABCDCSomMYpGBA54N5jAF1E+I
# E622i8UqrKbgdJ8X09eWmwIGZ9gU1QF/GBMyMDI1MDQxMjAwMjMwNS4xODdaMASA
# AgH0oIHgpIHdMIHaMQswCQYDVQQGEwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQ
# MA4GA1UEBxMHUmVkbW9uZDEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
# MSUwIwYDVQQLExxNaWNyb3NvZnQgQW1lcmljYSBPcGVyYXRpb25zMSYwJAYDVQQL
# Ex1UaGFsZXMgVFNTIEVTTjozREE1LTk2M0ItRTFGNDE1MDMGA1UEAxMsTWljcm9z
# b2Z0IFB1YmxpYyBSU0EgVGltZSBTdGFtcGluZyBBdXRob3JpdHmggg8gMIIHgjCC
# BWqgAwIBAgITMwAAAAXlzw//Zi7JhwAAAAAABTANBgkqhkiG9w0BAQwFADB3MQsw
# CQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMUgwRgYD
# VQQDEz9NaWNyb3NvZnQgSWRlbnRpdHkgVmVyaWZpY2F0aW9uIFJvb3QgQ2VydGlm
# aWNhdGUgQXV0aG9yaXR5IDIwMjAwHhcNMjAxMTE5MjAzMjMxWhcNMzUxMTE5MjA0
# MjMxWjBhMQswCQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0
# aW9uMTIwMAYDVQQDEylNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lc3RhbXBpbmcg
# Q0EgMjAyMDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoCggIBAJ5851Jj/eDF
# nwV9Y7UGIqMcHtfnlzPREwW9ZUZHd5HBXXBvf7KrQ5cMSqFSHGqg2/qJhYqOQxwu
# EQXG8kB41wsDJP5d0zmLYKAY8Zxv3lYkuLDsfMuIEqvGYOPURAH+Ybl4SJEESnt0
# MbPEoKdNihwM5xGv0rGofJ1qOYSTNcc55EbBT7uq3wx3mXhtVmtcCEr5ZKTkKKE1
# CxZvNPWdGWJUPC6e4uRfWHIhZcgCsJ+sozf5EeH5KrlFnxpjKKTavwfFP6XaGZGW
# UG8TZaiTogRoAlqcevbiqioUz1Yt4FRK53P6ovnUfANjIgM9JDdJ4e0qiDRm5sOT
# iEQtBLGd9Vhd1MadxoGcHrRCsS5rO9yhv2fjJHrmlQ0EIXmp4DhDBieKUGR+eZ4C
# NE3ctW4uvSDQVeSp9h1SaPV8UWEfyTxgGjOsRpeexIveR1MPTVf7gt8hY64XNPO6
# iyUGsEgt8c2PxF87E+CO7A28TpjNq5eLiiunhKbq0XbjkNoU5JhtYUrlmAbpxRjb
# 9tSreDdtACpm3rkpxp7AQndnI0Shu/fk1/rE3oWsDqMX3jjv40e8KN5YsJBnczyW
# B4JyeeFMW3JBfdeAKhzohFe8U5w9WuvcP1E8cIxLoKSDzCCBOu0hWdjzKNu8Y5Sw
# B1lt5dQhABYyzR3dxEO/T1K/BVF3rV69AgMBAAGjggIbMIICFzAOBgNVHQ8BAf8E
# BAMCAYYwEAYJKwYBBAGCNxUBBAMCAQAwHQYDVR0OBBYEFGtpKDo1L0hjQM972K9J
# 6T7ZPdshMFQGA1UdIARNMEswSQYEVR0gADBBMD8GCCsGAQUFBwIBFjNodHRwOi8v
# d3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL0RvY3MvUmVwb3NpdG9yeS5odG0wEwYD
# VR0lBAwwCgYIKwYBBQUHAwgwGQYJKwYBBAGCNxQCBAweCgBTAHUAYgBDAEEwDwYD
# VR0TAQH/BAUwAwEB/zAfBgNVHSMEGDAWgBTIftJqhSobyhmYBAcnz1AQT2ioojCB
# hAYDVR0fBH0wezB5oHegdYZzaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9w
# cy9jcmwvTWljcm9zb2Z0JTIwSWRlbnRpdHklMjBWZXJpZmljYXRpb24lMjBSb290
# JTIwQ2VydGlmaWNhdGUlMjBBdXRob3JpdHklMjAyMDIwLmNybDCBlAYIKwYBBQUH
# AQEEgYcwgYQwgYEGCCsGAQUFBzAChnVodHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20v
# cGtpb3BzL2NlcnRzL01pY3Jvc29mdCUyMElkZW50aXR5JTIwVmVyaWZpY2F0aW9u
# JTIwUm9vdCUyMENlcnRpZmljYXRlJTIwQXV0aG9yaXR5JTIwMjAyMC5jcnQwDQYJ
# KoZIhvcNAQEMBQADggIBAF+Idsd+bbVaFXXnTHho+k7h2ESZJRWluLE0Oa/pO+4g
# e/XEizXvhs0Y7+KVYyb4nHlugBesnFqBGEdC2IWmtKMyS1OWIviwpnK3aL5Jedwz
# beBF7POyg6IGG/XhhJ3UqWeWTO+Czb1c2NP5zyEh89F72u9UIw+IfvM9lzDmc2O2
# END7MPnrcjWdQnrLn1Ntday7JSyrDvBdmgbNnCKNZPmhzoa8PccOiQljjTW6GePe
# 5sGFuRHzdFt8y+bN2neF7Zu8hTO1I64XNGqst8S+w+RUdie8fXC1jKu3m9KGIqF4
# aldrYBamyh3g4nJPj/LR2CBaLyD+2BuGZCVmoNR/dSpRCxlot0i79dKOChmoONqb
# MI8m04uLaEHAv4qwKHQ1vBzbV/nG89LDKbRSSvijmwJwxRxLLpMQ/u4xXxFfR4f/
# gksSkbJp7oqLwliDm/h+w0aJ/U5ccnYhYb7vPKNMN+SZDWycU5ODIRfyoGl59BsX
# R/HpRGtiJquOYGmvA/pk5vC1lcnbeMrcWD/26ozePQ/TWfNXKBOmkFpvPE8CH+Ee
# GGWzqTCjdAsno2jzTeNSxlx3glDGJgcdz5D/AAxw9Sdgq/+rY7jjgs7X6fqPTXPm
# aCAJKVHAP19oEjJIBwD1LyHbaEgBxFCogYSOiUIr0Xqcr1nJfiWG2GwYe6ZoAF1b
# MIIHljCCBX6gAwIBAgITMwAAAEYX5HV6yv3a5QAAAAAARjANBgkqhkiG9w0BAQwF
# ADBhMQswCQYDVQQGEwJVUzEeMBwGA1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9u
# MTIwMAYDVQQDEylNaWNyb3NvZnQgUHVibGljIFJTQSBUaW1lc3RhbXBpbmcgQ0Eg
# MjAyMDAeFw0yNDExMjYxODQ4NDlaFw0yNTExMTkxODQ4NDlaMIHaMQswCQYDVQQG
# EwJVUzETMBEGA1UECBMKV2FzaGluZ3RvbjEQMA4GA1UEBxMHUmVkbW9uZDEeMBwG
# A1UEChMVTWljcm9zb2Z0IENvcnBvcmF0aW9uMSUwIwYDVQQLExxNaWNyb3NvZnQg
# QW1lcmljYSBPcGVyYXRpb25zMSYwJAYDVQQLEx1UaGFsZXMgVFNTIEVTTjozREE1
# LTk2M0ItRTFGNDE1MDMGA1UEAxMsTWljcm9zb2Z0IFB1YmxpYyBSU0EgVGltZSBT
# dGFtcGluZyBBdXRob3JpdHkwggIiMA0GCSqGSIb3DQEBAQUAA4ICDwAwggIKAoIC
# AQCwlXzoj/MNL1BfnV+gg4d0fZum1HdUJidSNTcDzpHJvmIBqH566zBYcV0TyN7+
# 3qOnJjpoTx6JBMgNYnL5BmTX9HrmX0WdNMLf74u7NtBSuAD2sf6n2qUUrz7i8f7r
# 0JiZixKJnkvA/1akLHppQMDCug1oC0AYjd753b5vy1vWdrHXE9hL71BZe5DCq5/4
# LBny8aOQZlzvjewgONkiZm+SfctkJjh9LxdkDlq5EvGE6YU0uC37XF7qkHvIksD2
# +XgBP0lEMfmPJo2fI9FwIA9YMX7KIINEM5OY6nkvKryM9s5bK6LV4z48NYpiI1xv
# H15YDps+19nHCtKMVTZdB4cYhA0dVqJ7dAu4VcxUwD1AEcMxWbIOR1z6OFkVY9GX
# 5oH8k17d9t35PWfn0XuxW4SG/rimgtFgpE/shRsy5nMCbHyeCdW0He1plrYQqTsS
# HP2n/lz2DCgIlnx+uvPLVf5+JG/1d1i/LdwbC2WH6UEEJyZIl3a0YwM4rdzoR+P4
# dO9I/2oWOxXCYqFytYdCy9ljELUwbyLjrjRddteR8QTxrCfadKpKfFY6Ak/HNZPU
# HaAPak3baOIvV7Q8axo3DWQy2ib3zXV6hMPNt1v90pv+q9daQdwUzUrgcbwThdrR
# hWHwlRIVg2sR668HPn4/8l9ikGokrL6gAmVxNswEZ9awCwIDAQABo4IByzCCAccw
# HQYDVR0OBBYEFBE20NSvdrC6Z6cm6RPGP8YbqIrxMB8GA1UdIwQYMBaAFGtpKDo1
# L0hjQM972K9J6T7ZPdshMGwGA1UdHwRlMGMwYaBfoF2GW2h0dHA6Ly93d3cubWlj
# cm9zb2Z0LmNvbS9wa2lvcHMvY3JsL01pY3Jvc29mdCUyMFB1YmxpYyUyMFJTQSUy
# MFRpbWVzdGFtcGluZyUyMENBJTIwMjAyMC5jcmwweQYIKwYBBQUHAQEEbTBrMGkG
# CCsGAQUFBzAChl1odHRwOi8vd3d3Lm1pY3Jvc29mdC5jb20vcGtpb3BzL2NlcnRz
# L01pY3Jvc29mdCUyMFB1YmxpYyUyMFJTQSUyMFRpbWVzdGFtcGluZyUyMENBJTIw
# MjAyMC5jcnQwDAYDVR0TAQH/BAIwADAWBgNVHSUBAf8EDDAKBggrBgEFBQcDCDAO
# BgNVHQ8BAf8EBAMCB4AwZgYDVR0gBF8wXTBRBgwrBgEEAYI3TIN9AQEwQTA/Bggr
# BgEFBQcCARYzaHR0cDovL3d3dy5taWNyb3NvZnQuY29tL3BraW9wcy9Eb2NzL1Jl
# cG9zaXRvcnkuaHRtMAgGBmeBDAEEAjANBgkqhkiG9w0BAQwFAAOCAgEAFIW5L+gG
# zX4gyHorS33YKXuK9iC91iZTpm30x/EdHG6U8NAu2qityxjZVq6MDq300gspG0nt
# zLYqVhjfku7iNzE78k6tNgFCr9wvGkIHeK+Q2RAO9/s5R8rhNC+lywOB+6K5Zi0k
# fO0agVXf7Nk2O6F6D9AEzNLijG+cOe5Ef2F5l4ZsVSkLFCI5jELC+r4KnNZjunc+
# qvjSz2DkNsXfrjFhyk+K7v7U7+JFZ8kZ58yFuxEX0cxDKpJLxiNh/ODCOL2UxYkh
# yfI3AR0EhfxX9QZHVgxyZwnavR35FxqLSiGTeAJsK7YN3bIxyuP6eCcnkX8TMdpu
# 9kPD97sHnM7po0UQDrjaN7etviLDxnax2nemdvJW3BewOLFrD1nSnd7ZHdPGPB3o
# WTCaK9/3XwQERLi3Xj+HZc89RP50Nt7h7+3G6oq2kXYNidI9iWd+gL+lvkQZH9YT
# IfBCLWjvuXvUUUU+AvFI00UtqrvdrIdqCFaqE9HHQgSfXeQ53xLWdMCztUP/YnMX
# iJxNBkc6UE2px/o6+/LXJDIpwIXR4HSodLfkfsNQl6FFrJ1xsOYGSHvcFkH8389R
# mUvrjr1NBbdesc4Bu4kox+3cabOZc1zm89G+1RRL2tReFzSMlYSGO3iKn3GGXmQi
# RmFlBb3CpbUVQz+fgxVMfeL0j4LmKQfT1jIxggPUMIID0AIBATB4MGExCzAJBgNV
# BAYTAlVTMR4wHAYDVQQKExVNaWNyb3NvZnQgQ29ycG9yYXRpb24xMjAwBgNVBAMT
# KU1pY3Jvc29mdCBQdWJsaWMgUlNBIFRpbWVzdGFtcGluZyBDQSAyMDIwAhMzAAAA
# RhfkdXrK/drlAAAAAABGMA0GCWCGSAFlAwQCAQUAoIIBLTAaBgkqhkiG9w0BCQMx
# DQYLKoZIhvcNAQkQAQQwLwYJKoZIhvcNAQkEMSIEIC2rKd+6XTaxYat+R1eKr2Mo
# 9pzGF5/MbQcwH/uT7ItrMIHdBgsqhkiG9w0BCRACLzGBzTCByjCBxzCBoAQgEid2
# SJpUPj5xQm73M4vqDmVh1QR6TiuTUVkL3P8Wis4wfDBlpGMwYTELMAkGA1UEBhMC
# VVMxHjAcBgNVBAoTFU1pY3Jvc29mdCBDb3Jwb3JhdGlvbjEyMDAGA1UEAxMpTWlj
# cm9zb2Z0IFB1YmxpYyBSU0EgVGltZXN0YW1waW5nIENBIDIwMjACEzMAAABGF+R1
# esr92uUAAAAAAEYwIgQgGd6N5twke92w10C72kzN6/8Nu/C/JDoe6Z+f9Crtobcw
# DQYJKoZIhvcNAQELBQAEggIAYEfaN6yWHoCoNFy2wM1ipqXykpArePm9L0kPCWb2
# Oo2IGlPG/Eq0lpW+RvAprgzfz/CQRbbUrkt2WBsgJURBfZNdQYrC1VNrX0Igntgb
# elvbtWMKemgaJUpnSdDbmMsB3NmwjUKtus677KW2G4UBZLrhMbyoTMdK7R+HuRJi
# cmbP/cC5Z+3kYAFphjO69ngaQv3glXvh+kiyRNr4l/06G4g84qyOuwkCDnbohJyA
# XggEu4szBk5rwQWMYb5bUyE3dukvogUkG0iH4tuFq24wP73KKaoGJU8gad4DCw5B
# LFnSND/5msbA0KsfWpIxAAaj7KO2tuWZDmXlYlTE63s+8kGGudhwZrjYuYAjS15t
# IUbhyYVQFmksDhZKA7gV/+FwIUutiwUNLjz1xPbnW0L3sa0pEZYPKs/4QqwrXY7y
# 0SGKfW6EUXOjRmygC74CYgT+iJbO+/MwVajF13JEQYl60cdx8AQthNXHZcd7PVSm
# WmygbuDRZzQ9TKv/vurX07WOaaU2PLiPkGaE7gxVmuKef6gFlAyTzX1Db6+sTCye
# 05x3Zzwfg8k8r5s3VPEwv9nX47iWa7VgLeqZzekwwRyDd1dYSzTQwG7woCPbG8F1
# Rz6HzVmXSFeDb2HgYfB9ABr5ulAY+bLOCNPBjRUVc8ekTLUvPxljKNNEGLFcch7+
# /2o=
# SIG # End signature block
