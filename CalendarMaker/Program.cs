using CalendarMaker;

Console.WriteLine("Welcome to Calendar Maker!");

var calendarMaker = new CalendarController();

var result = calendarMaker.StartCalendarMaker();
while(!result) {
    result = calendarMaker.StartCalendarMaker();
}