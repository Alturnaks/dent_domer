namespace Dental.Schedule;

public enum AppointmentStatus { Scheduled, Confirmed, Arrived, InChair, Completed, Cancelled, NoShow }
public enum AppointmentSource { Admin, Phone, Online, WalkIn }
public enum WaitlistStatus { Waiting, Offered, Booked, Cancelled }
