#!/usr/bin/env ruby
# Update World releases without changing the save/mod compatibility version.

require 'optparse'

ROOT = File.expand_path('..', __dir__)
VERSION_RE = /\A\d+\.\d+\.\d+\z/
COPIES = {
  'WRogue/SetupConfig.cs' => [
    /public const string WORLD_VERSION = "(?<version>\d+\.\d+\.\d+)";/
  ],
  'WRogue/RogueSurvivor.csproj' => [
    /<ApplicationVersion>(?<version>\d+\.\d+\.\d+)\.0<\/ApplicationVersion>/
  ],
  'README.txt' => [
    /Current World version: (?<version>\d+\.\d+\.\d+)/
  ]
}.freeze
COMPATIBILITY_COPIES = {
  'WRogue/Properties/AssemblyInfo.cs' => [
    /\[assembly: AssemblyVersion\("(?<version>\d+\.\d+\.\d+)\.0"\)\]/
  ],
  'WRogue/mods/Deonapocalypse/authors.json' => [
    /"game_version": "(?<version>\d+\.\d+\.\d+)"/
  ],
  'README.txt' => [
    /The game_version value must name this version \((?<version>\d+\.\d+\.\d+)\)/
  ],
  'docs/save-format.txt' => [
    /Saves record compatibility version (?<version>\d+\.\d+\.\d+)/
  ]
}.freeze

def valid_version(value)
  unless VERSION_RE.match?(value) && value.split('.').all? { |part| part.to_i <= 65_535 }
    raise ArgumentError, 'version must be three numeric components from 0 to 65535'
  end
  value
end

def updated_copy(path, patterns, version, check)
  updated = File.read(path, encoding: 'UTF-8')
  patterns.each do |pattern|
    matches = updated.to_enum(:scan, pattern).map { Regexp.last_match }
    unless matches.length == 1
      raise ArgumentError, "#{path}: expected exactly one match for #{pattern.inspect}"
    end
    match = matches.first
    if check && match[:version] != version
      raise ArgumentError, "#{path}: found #{match[:version]}, expected #{version}"
    end
    updated[match.begin(:version)...match.end(:version)] = version
  end
  updated
end

def main
  check = false
  parser = OptionParser.new do |options|
    options.banner = 'Usage: ruby tools/release_version.rb [--check | X.Y.Z]'
    options.on('--check', 'Verify copies against VERSION') { check = true }
    options.on('-h', '--help', 'Show this help') { puts options; exit }
  end
  parser.parse!
  unless (check && ARGV.empty?) || (!check && ARGV.length == 1)
    raise ArgumentError, 'use either --check or a new version'
  end

  version_file = File.join(ROOT, 'VERSION')
  version = valid_version(check ? File.read(version_file, encoding: 'UTF-8').strip : ARGV.first)
  updates = COPIES.to_h do |relative, patterns|
    path = File.join(ROOT, relative)
    [path, updated_copy(path, patterns, version, check)]
  end
  setup = File.read(File.join(ROOT, 'WRogue/SetupConfig.cs'), encoding: 'UTF-8')
  compatibility = setup.match(/public const string GAME_VERSION = "(?<version>\d+\.\d+\.\d+)";/)
  raise ArgumentError, 'save/mod compatibility version is missing' unless compatibility
  valid_version(compatibility[:version])
  COMPATIBILITY_COPIES.each do |relative, patterns|
    updated_copy(File.join(ROOT, relative), patterns, compatibility[:version], true)
  end
  unless check
    updates.each do |path, content|
      File.write(path, content, encoding: 'UTF-8') if File.read(path, encoding: 'UTF-8') != content
    end
    File.write(version_file, "#{version}\n", encoding: 'UTF-8')
  end

  tag = ENV['GITHUB_REF_TYPE'] == 'tag' ? ENV.fetch('GITHUB_REF_NAME', '') : ''
  if check && tag.start_with?('world-v') && tag != "world-v#{version}"
    raise ArgumentError, "release tag #{tag} does not match VERSION #{version}"
  end
  puts "World #{version}: #{check ? 'all copies match' : 'release files updated'}; compatibility #{compatibility[:version]}"
end

begin
  main
rescue ArgumentError, OptionParser::ParseError, SystemCallError => error
  warn "release_version: #{error.message}"
  exit 1
end
