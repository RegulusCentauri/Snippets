using System.Drawing;
using System.Globalization;

using OfficeOpenXml;
using OfficeOpenXml.Style;


namespace CalendarMaker {
    // https://www.epplussoftware.com/en/Developers/
    // https://github.com/EPPlusSoftware/EPPlus/wiki/Custom-Table-Styles
    // https://epplussoftware.com/docs/6.2/api/index.html
    public class CalendarController {
        public const int PointsInInches = 72;

        public bool StartCalendarMaker() {
            try {
                var monthsToProcess = new List<int>();
                var endAppTexts = new List<string>() { "end", "exit", "e", "no", "n", "fu", "konec"};
                Console.WriteLine();
                Console.WriteLine("Please input the calendar year");
                var inputYear = Console.ReadLine();
                if(string.IsNullOrEmpty(inputYear)) {
                    Console.WriteLine($"Setting year to {DateTime.Now.Year}");
                    inputYear = $"{DateTime.Now.Year}";
                }
                else if(endAppTexts.Contains(inputYear.ToLower())) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Closing");
                    Console.ResetColor();
                    Environment.Exit(0);
                }
                else if(inputYear.Length != 4) {
                    Console.WriteLine($"The input year {inputYear} was not 4 digits long, please go again");
                    return false;
                }
                else if(!int.TryParse(inputYear, out _)) {
                    Console.WriteLine($"The input year was not an int {inputYear}, please go again");
                    return false;
                }
                int.TryParse(inputYear, out var year);
                Console.WriteLine();
                Console.WriteLine("Please input the number(s) of month(s) you want to create in the following format");
                Console.WriteLine("4,8 for April to August OR 5 for May - all other inputs will be scrapped");
                var inputString = Console.ReadLine();
                if(string.IsNullOrEmpty(inputString)) {
                    Console.WriteLine("The input was empty, please go again");
                    return false;
                }
                else if(endAppTexts.Contains(inputString.ToLower())) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Closing");
                    Console.ResetColor();
                    Environment.Exit(0);
                }
                else if(inputString.Length > 5) { //10,12
                    Console.WriteLine($"The input {inputString} was of incorrect length of {inputString.Length} characters, please go again");
                    return false;
                }

                if(inputString!.Length == 1 || inputString!.Length == 2) {
                    if(int.TryParse(inputString, out var monthNr)) {
                        if(monthNr == 0) {
                            Console.WriteLine($"There's no such thing as nullth month, try again");
                            return false;
                        }
                        monthsToProcess.Add(monthNr);
                        monthsToProcess.Add(monthNr);
                        CreateCalendar(monthsToProcess, year);
                        return true;
                    }
                    else {
                        Console.WriteLine($"The input {inputString} was not a number, try again");
                        return false;
                    }
                }
                else {
                    var inputNumbers = inputString.Split(',');
                    if(inputNumbers.Length != 2) {
                        Console.WriteLine($"The input {inputString} was not correct, the delimeter wasn't a comma, try again");
                        return false;
                    }
                    else if(int.TryParse(inputNumbers[0], out var firstNr) && int.TryParse(inputNumbers[1], out var secondNr)) {
                        monthsToProcess.Add(firstNr);
                        monthsToProcess.Add(secondNr);
                        CreateCalendar(monthsToProcess, year);
                        return true;
                    }
                    else {
                        Console.WriteLine($"One of the expected numbers was not a number {inputNumbers[0]},{inputNumbers[1]}, try again");
                        return false;
                    }
                }
            }
            catch(Exception e) {
                Console.WriteLine($"Something went wrong {e}");
                return false;
            }
        }

        public void CreateCalendar(List<int> numbersList, int year) {
            var oneMonth = numbersList[0] == numbersList[1];
            if((oneMonth && numbersList[0] > 12) || (!oneMonth && (numbersList[0] > 12 || numbersList[1] > 12))) {
                Console.WriteLine($"There are only 12 months in a year, try again");
                throw new Exception();
            }
            if(!oneMonth && numbersList[0] > numbersList[1]) {
                var firstMonth = numbersList[0];
                numbersList[0] = numbersList[1];
                numbersList[1] = firstMonth;
            }
            Console.WriteLine($"Starting to create calendar for the month(s) {(oneMonth ? DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName(numbersList[0]) : (DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName(numbersList[0]) + "-" + DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName(numbersList[1])))}");
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            var monthsList = Enumerable.Range(numbersList[0], numbersList[1] - numbersList[0] + 1).ToList();
            foreach(var monthNr in monthsList) {
                var monthName = DateTimeFormatInfo.CurrentInfo.GetAbbreviatedMonthName(monthNr);
                monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);
                var monthNameCs = new DateTime(year, monthNr, 1).ToString("MMMM", CultureInfo.CreateSpecificCulture("cs"));
                monthNameCs = char.ToUpper(monthNameCs[0]) + monthNameCs.Substring(1);
                var nrOfSundays = 0;

                var newFile = new FileInfo($"{year}_{monthNr}_{monthName}.xlsx");
                if(newFile.Exists) {
                    newFile.Delete();  // ensures we create a new workbook
                    newFile = new FileInfo($"{year}_{monthNr}_{monthName}.xlsx");
                }
                using(var package = new ExcelPackage(newFile)) {
                    var worksheet = package.Workbook.Worksheets.Add(monthName);

                    //Column width
                    worksheet.Columns[1].Width = 3.71; //A
                    worksheet.Columns[2].Width = 3.14; //B
                    worksheet.Columns[3].Width = 9.71; //C
                    worksheet.Columns[4].Width = 7.29; //D
                    worksheet.Columns[5].Width = 6.77; //E
                    worksheet.Columns[6].Width = 7.29; //F
                    worksheet.Columns[7].Width = 8.4; //G
                    worksheet.Columns[8].Width = 3.71; //H
                    worksheet.Columns[9].Width = 3.14; //I
                    worksheet.Columns[10].Width = 9.71; //J
                    worksheet.Columns[11].Width = 7.29; //K
                    worksheet.Columns[12].Width = 6.77; //L
                    worksheet.Columns[13].Width = 7.29; //M

                    //Cell content
                    var monthLength = DateTime.DaysInMonth(year, monthNr);
                    var monthHalf = Math.Floor((double)(monthLength / 2));
                    worksheet.Rows[1, 1].Height = InchesToPoints(0.22); //72 points in one inch
                    for(int i = 0 ; i <= monthLength ; i++) {
                        if(i == 0) {
                            var cellRange = worksheet.Cells[1, 1, 1, 6];
                            cellRange.Merge = true;
                            cellRange.Style.Font.Bold = true;
                            cellRange.Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.Black);
                            cellRange.Value = $"{monthNameCs}";
                            cellRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                        }
                        else {
                            var dayName = new DateTime(year, monthNr, i).ToString("ddd", CultureInfo.CreateSpecificCulture("cs"));
                            if(dayName == "ne") nrOfSundays += 1;
                            var isOverHalf = i > monthHalf;
                            var cellShiftBasedOnColumn = !isOverHalf ? 0 : 7;
                            var rowNr = !isOverHalf ? i + 1 : (int)(i - monthHalf + 1);
                            if(!isOverHalf || (i == monthLength && monthLength % 2 == 1)) worksheet.Rows[rowNr, rowNr].Height = InchesToPoints(0.59);
                            worksheet.Rows[rowNr, rowNr].Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                            worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Value = dayName;
                            worksheet.Cells[rowNr, 2 + cellShiftBasedOnColumn].Value = $"{i}.";

                            //Adjust border of the first two columns
                            //First, set all day boxes with border
                            worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn, rowNr, 6 + cellShiftBasedOnColumn].Style.Border.BorderAround(ExcelBorderStyle.Thin, Color.Black);
                            if(i == 1) {
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Style.Border.Bottom.Style = ExcelBorderStyle.None;
                                worksheet.Cells[rowNr, 2 + cellShiftBasedOnColumn].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            }
                            if(i > 1) {
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Style.Border.Bottom.Style = ExcelBorderStyle.None;
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Style.Border.Top.Style = ExcelBorderStyle.None;
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Style.Border.Right.Style = ExcelBorderStyle.None;

                                worksheet.Cells[rowNr, 2 + cellShiftBasedOnColumn].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                            }
                            //If last day of first or second column of days - set bottom border
                            if((!isOverHalf && i == monthHalf) || (isOverHalf && i == monthLength)) {
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                            }
                            //If first day of second column of days - set top border
                            if(isOverHalf && i - monthHalf == 1) {
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                            }
                            //Remove the left border of cells with day number
                            worksheet.Cells[rowNr, 2 + cellShiftBasedOnColumn].Style.Border.Left.Style = ExcelBorderStyle.None;

                            //Background color for Sundays
                            if(dayName == "ne") {
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn, rowNr, 6 + cellShiftBasedOnColumn].Style.Fill.PatternType = ExcelFillStyle.Solid;
                                worksheet.Cells[rowNr, 1 + cellShiftBasedOnColumn, rowNr, 6 + cellShiftBasedOnColumn].Style.Fill.BackgroundColor.SetColor(1, 255, 242, 204);
                            }

                            //Insert radioactive image every day
                            string workingDirectory = Environment.CurrentDirectory;
                            string projectDirectory = Directory.GetParent(workingDirectory).Parent.Parent.FullName;
                            string radioactiveImgPath = projectDirectory + "\\images\\Radioactive_Smaller.png";
                            int yOffsetToxic = 20;
                            int xOffsetToxic = 10;
                            int yOffset = 15;
                            int xOffset = 8;
                            var radioactiveImg = worksheet.Drawings.AddPicture($"Radioactive{i}", new FileInfo(radioactiveImgPath));
                            radioactiveImg.SetSize(70);
                            radioactiveImg.SetPosition(rowNr - 1, yOffsetToxic - 2, 6 + cellShiftBasedOnColumn - 1, xOffsetToxic);

                            //Insert other images
                            //if(dayName == "so") {
                            //    string hamsterImgPath = projectDirectory + "\\images\\hamster_wheel.png";
                            //    var hamsterImage = worksheet.Drawings.AddPicture($"Hamster{i}", new FileInfo(hamsterImgPath));
                            //    hamsterImage.SetSize(85);
                            //    hamsterImage.SetPosition(rowNr - 1, yOffset, 5 + cellShiftBasedOnColumn - 1, xOffset);
                            //}

                            if(!isOverHalf && rowNr == 3) { //Second day of the month
                                string btcImgPath = projectDirectory + "\\images\\money.png";
                                var btcImage = worksheet.Drawings.AddPicture($"Money{i}", new FileInfo(btcImgPath));
                                btcImage.SetSize(85);
                                btcImage.SetPosition(rowNr - 1, yOffset - 1, 4 - 1, xOffset - 2);
                            }

                            //Every first and third Sunday
                            if(dayName == "ne" && (nrOfSundays == 1 || nrOfSundays == 3)) {
                                string mopImgPath = projectDirectory + "\\images\\mop.png";
                                var mopImage = worksheet.Drawings.AddPicture($"Mop{i}", new FileInfo(mopImgPath));
                                mopImage.SetSize(85);
                                mopImage.SetPosition(rowNr - 1, yOffset - 4, 5 + cellShiftBasedOnColumn - 1, xOffset);
                            }

                            //Every second Sunday
                            if(dayName == "ne" && nrOfSundays == 2) {
                                string sinkImgPath = projectDirectory + "\\images\\sink.png";
                                var sinkImage = worksheet.Drawings.AddPicture($"Sink{i}", new FileInfo(sinkImgPath));
                                sinkImage.SetSize(85);
                                sinkImage.SetPosition(rowNr - 1, yOffset, 5 + cellShiftBasedOnColumn - 1, xOffset);
                            }

                            //Last Sunday of every even month
                            if(dayName == "ne" && (monthNr % 2 == 0) && nrOfSundays == 4) {
                                string bedImgPath = projectDirectory + "\\images\\bed_and_towel.png";
                                var bedImage = worksheet.Drawings.AddPicture($"BedLinen{i}", new FileInfo(bedImgPath));
                                bedImage.SetSize(85);
                                bedImage.SetPosition(rowNr - 1, yOffset, 5 + cellShiftBasedOnColumn - 1, xOffset - 1);
                            }
                        }
                    }
                    //Description part
                    var descRowNr = (int)(1 + Math.Ceiling((double)(monthLength / 2)) + 2);
                    int yOffsetDesc = 12;
                    worksheet.Rows[descRowNr, descRowNr].Height = InchesToPoints(0.59);
                    string workingDirectoryDesc = Environment.CurrentDirectory;
                    string projectDirectoryDesc = Directory.GetParent(workingDirectoryDesc).Parent.Parent.FullName;

                    //string hamsterImgPathDesc = projectDirectoryDesc + "\\images\\hamster_wheel.png";
                    //var hamsterImageDesc = worksheet.Drawings.AddPicture($"HamsterDesc", new FileInfo(hamsterImgPathDesc));
                    //hamsterImageDesc.SetSize(85);
                    //hamsterImageDesc.SetPosition(descRowNr - 1, yOffsetDesc, 0, 8);
                    //worksheet.Cells[descRowNr + 1, 1].Value = "žblobík";

                    string radioactiveImgPathDesc = projectDirectoryDesc + "\\images\\Radioactive_Smaller.png";
                    var radioactiveImgDesc = worksheet.Drawings.AddPicture($"RadioactiveDesc", new FileInfo(radioactiveImgPathDesc));
                    radioactiveImgDesc.SetSize(85);
                    radioactiveImgDesc.SetPosition(descRowNr - 1, yOffsetDesc, 3 - 1, 45);
                    worksheet.Cells[descRowNr + 1, 3].Value = "         čiči záchod";

                    string btcImgPathDesc = projectDirectoryDesc + "\\images\\money.png";
                    var btcImageDesc = worksheet.Drawings.AddPicture($"MoneyDesc", new FileInfo(btcImgPathDesc));
                    btcImageDesc.SetSize(85);
                    btcImageDesc.SetPosition(descRowNr - 1, yOffsetDesc + 3, 5 - 1, 27);
                    worksheet.Cells[descRowNr + 1, 5].Value = "peníze na účet";

                    string mopImgPathDesc = projectDirectoryDesc + "\\images\\mop.png";
                    var mopImageDesc = worksheet.Drawings.AddPicture($"MopDesc", new FileInfo(mopImgPathDesc));
                    mopImageDesc.SetSize(85);
                    mopImageDesc.SetPosition(descRowNr - 1, yOffsetDesc, 7 - 1, 33);
                    worksheet.Cells[descRowNr + 1, 7].Value = "      lux,prach";

                    string sinkImgPathDesc = projectDirectoryDesc + "\\images\\sink.png";
                    var sinkImageDesc = worksheet.Drawings.AddPicture($"SinkDesc", new FileInfo(sinkImgPathDesc));
                    sinkImageDesc.SetSize(85);
                    sinkImageDesc.SetPosition(descRowNr - 1, yOffsetDesc, 10 - 1, 26);
                    worksheet.Cells[descRowNr + 1, 9].Value = "     záchod,koupelna";

                    string bedImgPathDesc = projectDirectoryDesc + "\\images\\bed_and_towel.png";
                    var bedImageDesc = worksheet.Drawings.AddPicture($"BedLinenDesc", new FileInfo(bedImgPathDesc));
                    bedImageDesc.SetSize(85);
                    bedImageDesc.SetPosition(descRowNr - 1, yOffsetDesc + 2, 12 - 1, 27);
                    worksheet.Cells[descRowNr + 1, 12].Value = " peřiny,ručníky";

                    package.SaveAs(newFile);
                }
            }
        }

        public double InchesToPoints(int nr) {
            return nr * PointsInInches;
        }

        public double InchesToPoints(double nr) {
            return nr * PointsInInches;
        }
    }


}
