using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;

class Program
{
    static void Main(string[] args)
    {

        string basePath = @"C:\Users\gyu\source\marcreader\data"; 
        
        string qa_naco = $@"{basePath}\naco.aut.data.d20250122qa"; // First MARC file
        string pr_naco = $@"{basePath}\naco.aut.data.d250122prod"; // Second MARC file
        string reportFilePath = $@"{basePath}\comparison_report.txt"; // Output report file

        
        qa_naco = $@"{basePath}\SI-NEW.naco.aut.data.d20250205";
        pr_naco = $@"{basePath}\pr.naco.aut.data.d250205";
       //pr_naco = $@"{basePath}\SI-NEW.naco.aut.data.d20250205";
        reportFilePath = $@"{basePath}\naco_comparison_report-d250205.txt";
        

        string fileCompareResult = "";
        var differences = 0;

        // Parse both MARC files
        List<MarcRecord> records1 = ParseMarcFile(qa_naco);
        List<MarcRecord> records2 = ParseMarcFile(pr_naco);

        // Display total number of records in each file
        Console.WriteLine("----- Comparison Report -----");
        Console.WriteLine();
        Console.WriteLine($"Total records in QA file: {records1.Count}");
        Console.WriteLine($"Total records in PROD file: {records2.Count}");
        Console.WriteLine();

        var matchedRecords = new List<(MarcRecord, MarcRecord)>();
        //var unmatchedRecords1 = new List<MarcRecord>();
        //var unmatchedRecords2 = new List<MarcRecord>();
        
       // Match records by control number (001 field)
        matchedRecords = MatchRecords(records1, records2);
    
        Console.WriteLine($"Matched records: {matchedRecords.Count}");

        fileCompareResult += $"{Environment.NewLine}QA records: {records1.Count}; Prod records: {records2.Count}";
      

        if (records1.Count != records2.Count) {
            fileCompareResult = "The number of records are not the same from the two files. "; 
            var (unmatchedRecords1, unmatchedRecords2) = FindUnmatchedRecords(records1, records2);    

            if (unmatchedRecords1.Count > 0 )
            {
                fileCompareResult += $"{Environment.NewLine}The file from QA has {unmatchedRecords1.Count} extra records. They are: ";
                foreach (var r in unmatchedRecords1)
                {
                    fileCompareResult += $"{Environment.NewLine}- Control number: {r.ControlNumber}.";   
                }                
            }

            if (unmatchedRecords2.Count > 0 )
            {
                fileCompareResult += $"{Environment.NewLine}- The file from QA has {unmatchedRecords2.Count} extra records. They are:";      
                foreach (var r in unmatchedRecords2)
                {
                     fileCompareResult += $"{Environment.NewLine}- Control number: {r.ControlNumber}.";   
                }                        
            }

        }

        else if (records1.Count == records2.Count && records1.Count == matchedRecords.Count)
        {
            
            fileCompareResult = $"The two file have the same number of records and every record matches based on the control number. ";        
        }
        
        

        Console.WriteLine (fileCompareResult);         

        // Generate the comparison report
        GenerateComparisonReport(matchedRecords);

            // Generate the comparison report and write it to a file
        GenerateComparisonReport(matchedRecords, reportFilePath, qa_naco, pr_naco, fileCompareResult, out differences);

        if (differences == 0) {
            Console.WriteLine("The two files are identical.");
        }
        else
        {
            Console.WriteLine($"There are {differences} differences.");
        }

        Console.WriteLine($"Report written to: {reportFilePath}");
    }

    static List<MarcRecord> ParseMarcFile(string filePath)
    {
        List<MarcRecord> records = new List<MarcRecord>();
        byte[] fileBytes = File.ReadAllBytes(filePath);

        int position = 0;
        while (position < fileBytes.Length)
        {
            // Read the record length (first 5 bytes of the leader)
            string recordLengthStr = Encoding.UTF8.GetString(fileBytes, position, 5);
            int recordLength = int.Parse(recordLengthStr);

            // Extract the record data
            byte[] recordBytes = new byte[recordLength];
            Array.Copy(fileBytes, position, recordBytes, 0, recordLength);
            position += recordLength;

            // Parse the record
            MarcRecord record = ParseMarcRecord(recordBytes);
            records.Add(record);
        }

        return records;
    }

    static MarcRecord ParseMarcRecord(byte[] recordBytes)
    {
        MarcRecord record = new MarcRecord();

        // Read the leader (first 24 bytes)
        record.Leader = Encoding.UTF8.GetString(recordBytes, 0, 24);

        // Read the directory
        int baseAddress = int.Parse(Encoding.UTF8.GetString(recordBytes, 12, 5));
        int directoryEnd = baseAddress - 1;

        int dirPosition = 24;
        while (dirPosition < directoryEnd)
        {
            // Read the directory entry (12 bytes per entry)
            string tag = Encoding.UTF8.GetString(recordBytes, dirPosition, 3);
            int fieldLength = int.Parse(Encoding.UTF8.GetString(recordBytes, dirPosition + 3, 4));
            int fieldStart = int.Parse(Encoding.UTF8.GetString(recordBytes, dirPosition + 7, 5));

            // Extract the field data
            string fieldData = Encoding.UTF8.GetString(recordBytes, baseAddress + fieldStart, fieldLength);

            // Parse the field
            MarcField field = ParseMarcField(tag, fieldData);
            record.Fields.Add(field);

            // Store the control number (001 field) for matching
            if (tag == "001")
            {
                record.ControlNumber = fieldData;
            }

            dirPosition += 12;
        }

        return record;
    }

    static MarcField ParseMarcField(string tag, string fieldData)
    {
        MarcField field = new MarcField { Tag = tag };

        if (tag == "001" || tag == "003" || tag == "005" || tag == "008")
        {
            // Control field (no indicators or subfields)
            field.Value = fieldData;
        }
        else
        {
            // Data field (with indicators and subfields)
            field.Indicators = new char[] { fieldData[0], fieldData[1] };
            string subfieldData = fieldData.Substring(2);

            // Parse subfields
            field.Subfields = new List<MarcSubfield>();
            
            string[] subfields = subfieldData.Split('\u001f');  //\u001f is subfield delimiter
            for (int i = 1; i < subfields.Length; i++)
            {
                if (subfields[i].Length > 0)
                {
                    char subfieldCode = subfields[i][0];
                    string subfieldValue = subfields[i].Substring(1);
                    field.Subfields.Add(new MarcSubfield { Code = subfieldCode, Value = subfieldValue });
                }
            }
        }

        return field;
    }

    static List<(MarcRecord, MarcRecord)> MatchRecords(List<MarcRecord> records1, List<MarcRecord> records2)
    {
        // Create a dictionary to map control numbers to records
        var dict1 = records1.ToDictionary(r => r.ControlNumber);
        var dict2 = records2.ToDictionary(r => r.ControlNumber);

        // Match records by control number
        List<(MarcRecord, MarcRecord)> matchedRecords = new List<(MarcRecord, MarcRecord)>();
        foreach (var controlNumber in dict1.Keys.Intersect(dict2.Keys))
        {
            matchedRecords.Add((dict1[controlNumber], dict2[controlNumber]));
        }


        return matchedRecords;
    }

static (List<MarcRecord> onlyInRecords1, List<MarcRecord> onlyInRecords2) FindUnmatchedRecords(List<MarcRecord> records1, List<MarcRecord> records2)
{
    var dict1 = records1.ToDictionary(r => r.ControlNumber);
    var dict2 = records2.ToDictionary(r => r.ControlNumber);

    // Find control numbers only in dict1
    var onlyInDict1 = dict1.Keys.Except(dict2.Keys)
                                .Select(controlNumber => dict1[controlNumber])
                                .ToList();

    // Find control numbers only in dict2
    var onlyInDict2 = dict2.Keys.Except(dict1.Keys)
                                .Select(controlNumber => dict2[controlNumber])
                                .ToList();

    return (onlyInDict1, onlyInDict2);
}


    static void GenerateComparisonReport(List<(MarcRecord Record1, MarcRecord Record2)> matchedRecords)
    {
        Console.WriteLine("----- Comparison Report -----");
        Console.WriteLine();

        foreach (var (record1, record2) in matchedRecords)
        {
            Console.WriteLine($"Control Number: {record1.ControlNumber}");
            Console.WriteLine();

            // Compare leaders
            Console.WriteLine("Leader:");
            Console.WriteLine($"| {"File",-10} | {"Leader",-24} | {"Equal",-6} |");
            Console.WriteLine($"| {"QA",-10} | {record1.Leader,-24} | {Normalize(record1.Leader) == Normalize(record2.Leader),-6} |");
            Console.WriteLine($"| {"PROD",-10} | {record2.Leader,-24} | {Normalize(record1.Leader) == Normalize(record2.Leader),-6} |");
            Console.WriteLine();

            // Compare fields
            Console.WriteLine("Fields:");
            var fields1 = record1.Fields.GroupBy(f => f.Tag).ToDictionary(g => g.Key, g => g.ToList());
            var fields2 = record2.Fields.GroupBy(f => f.Tag).ToDictionary(g => g.Key, g => g.ToList());

            // Get all unique tags
            var allTags = fields1.Keys.Union(fields2.Keys).OrderBy(t => t);

            // Display table header
            Console.WriteLine($"| {"Tag",-5} | {"File",-10} | {"Indicators",-12} | {"Value/Subfields",-80} | {"Equal",-6} |");
            Console.WriteLine(new string('-', 122));

            foreach (var tag in allTags)
            {
                fields1.TryGetValue(tag, out var fields1List);
                fields2.TryGetValue(tag, out var fields2List);

                // Display fields from QA file
                if (fields1List != null)
                {
                    foreach (var field in fields1List)
                    {
                        string value = field.Tag == "001" || field.Tag == "003" || field.Tag == "005" || field.Tag == "008"
                            ? field.Value
                            : FormatSubfields(field.Subfields);
                        bool isEqual = fields2List != null && fields2List.Any(f => NormalizeField(field) == NormalizeField(f));
                        Console.WriteLine($"| {tag,-5} | {"QA",-10} | {new string(field.Indicators ?? new char[] { ' ', ' ' }),-12} | {value,-80} | {isEqual,-6} |");
                    }
                }
                else
                {
                    Console.WriteLine($"| {tag,-5} | {"QA",-10} | {"",-12} | {"MISSING",-80} | {"False",-6} |");
                }

                // Display fields from PROD file
                if (fields2List != null)
                {
                    foreach (var field in fields2List)
                    {
                        string value = field.Tag == "001" || field.Tag == "003" || field.Tag == "005" || field.Tag == "008"
                            ? field.Value
                            : FormatSubfields(field.Subfields);
                        bool isEqual = fields1List != null && fields1List.Any(f => NormalizeField(field) == NormalizeField(f));
                        Console.WriteLine($"| {tag,-5} | {"PROD",-10} | {new string(field.Indicators ?? new char[] { ' ', ' ' }),-12} | {value,-80} | {isEqual,-6} |");
                    }
                }
                else
                {
                    Console.WriteLine($"| {tag,-5} | {"PROD",-10} | {"",-12} | {"MISSING",-80} | {"False",-6} |");
                }

                Console.WriteLine(new string('-', 122));
            }

            Console.WriteLine();
        }
    }

static void GenerateComparisonReport(List<(MarcRecord Record1, MarcRecord Record2)> matchedRecords, string reportFilePath, string qaFilePath, string prFilePath, string compResult, out int differences)
    {
        int finalDifferences = 0; //final compare result 

        using (StreamWriter writer = new StreamWriter(reportFilePath))
        {
            writer.WriteLine("----- Comparison Report -----");
            writer.WriteLine();
            writer.WriteLine("The files compared: ");
            writer.WriteLine($"QA: {qaFilePath}");
            writer.WriteLine($"Prod: {prFilePath}");
            writer.WriteLine();
            writer.WriteLine(compResult);
   
            writer.WriteLine();
            writer.WriteLine();
            writer.WriteLine("The comparison details are below.");
            writer.WriteLine();

            foreach (var (record1, record2) in matchedRecords)
            {
                writer.WriteLine($"Control Number: {record1.ControlNumber}");
                writer.WriteLine();

                // Compare leaders
                writer.WriteLine("Leader:");
                writer.WriteLine($"| {"File",-10} | {"Leader",-24} | {"Equal",-6} |");
                
                if (Normalize(record1.Leader) != Normalize(record2.Leader))  {
                    finalDifferences++;
                }          

                writer.WriteLine($"| {"QA",-10} | {record1.Leader,-24} | {Normalize(record1.Leader) == Normalize(record2.Leader),-6} |");
                writer.WriteLine($"| {"PROD",-10} | {record2.Leader,-24} | {Normalize(record1.Leader) == Normalize(record2.Leader),-6} |");
                writer.WriteLine();

                // Compare fields
                writer.WriteLine("Fields:");
                var fields1 = record1.Fields.GroupBy(f => f.Tag).ToDictionary(g => g.Key, g => g.ToList());
                var fields2 = record2.Fields.GroupBy(f => f.Tag).ToDictionary(g => g.Key, g => g.ToList());

                // Get all unique tags
                var allTags = fields1.Keys.Union(fields2.Keys).OrderBy(t => t);

                // Display table header
                writer.WriteLine($"| {"Tag",-5} | {"File",-10} | {"Indicators",-12} | {"Value/Subfields",-80} | {"Equal",-6} |");
                writer.WriteLine(new string('-', 122));

                foreach (var tag in allTags)
                {
                    fields1.TryGetValue(tag, out var fields1List);
                    fields2.TryGetValue(tag, out var fields2List);

                    // Display fields from QA file
                    if (fields1List != null)
                    {
                        foreach (var field in fields1List)
                        {
                            string value = field.Tag == "001" || field.Tag == "003" || field.Tag == "005" || field.Tag == "008"
                                ? field.Value
                                : FormatSubfields(field.Subfields);
                            bool isEqual = fields2List != null && fields2List.Any(f => NormalizeField(field) == NormalizeField(f));
                            if (!isEqual)  { finalDifferences++;}   
                            writer.WriteLine($"| {tag,-5} | {"QA",-10} | {new string(field.Indicators ?? new char[] { ' ', ' ' }),-12} | {value,-80} | {isEqual,-6} |");
                        }
                    }
                    else
                    {
                        writer.WriteLine($"| {tag,-5} | {"QA",-10} | {"",-12} | {"MISSING",-80} | {"False",-6} |");
                    }

                    // Display fields from PROD file
                    if (fields2List != null)
                    {
                        foreach (var field in fields2List)
                        {
                            string value = field.Tag == "001" || field.Tag == "003" || field.Tag == "005" || field.Tag == "008"
                                ? field.Value
                                : FormatSubfields(field.Subfields);
                            bool isEqual = fields1List != null && fields1List.Any(f => NormalizeField(field) == NormalizeField(f));

                            if (!isEqual)  { finalDifferences++;}   

                            writer.WriteLine($"| {tag,-5} | {"PROD",-10} | {new string(field.Indicators ?? new char[] { ' ', ' ' }),-12} | {value,-80} | {isEqual,-6} |");
                        }
                    }
                    else
                    {
                        writer.WriteLine($"| {tag,-5} | {"PROD",-10} | {"",-12} | {"MISSING",-80} | {"False",-6} |");
                    }

                    writer.WriteLine(new string('-', 122));
                }

                writer.WriteLine();
            }

            differences = finalDifferences;
        }
    }

    static string FormatSubfields(List<MarcSubfield> subfields)
    {
        if (subfields == null || subfields.Count == 0)
            return string.Empty;

        return string.Join(" ", subfields.Select(sf => $"${sf.Code}: {sf.Value}"));
    }

    static string Normalize(string value)
    {
        return value?.Trim().ToLowerInvariant();
    }

    static string NormalizeField(MarcField field)
    {
        if (field.Tag == "001" || field.Tag == "003" || field.Tag == "005" || field.Tag == "008")
        {
            return Normalize(field.Value);
        }
        else
        {
            return Normalize(FormatSubfields(field.Subfields));
        }
    }
}

class MarcRecord
{
    public string Leader { get; set; }
    public string ControlNumber { get; set; }
    public List<MarcField> Fields { get; set; } = new List<MarcField>();
}

class MarcField
{
    public string Tag { get; set; }
    public char[] Indicators { get; set; }
    public List<MarcSubfield> Subfields { get; set; }
    public string Value { get; set; } // For control fields
}

class MarcSubfield
{
    public char Code { get; set; }
    public string Value { get; set; }
}