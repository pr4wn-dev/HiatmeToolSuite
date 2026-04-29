using System;
class P { static void Main() {
  var s = "https://portal.app.wellryde.com/portal/filterdata;jsessionid=05C475F3357D12CAA7069E3E51AAAF09";
  var u1 = new Uri(s, UriKind.Absolute);
  var u2 = new Uri(s, true);
  Console.WriteLine("UriKind.Absolute AbsolutePath=" + u1.AbsolutePath);
  Console.WriteLine("UriKind.Absolute AbsoluteUri=" + u1.AbsoluteUri);
  Console.WriteLine("dontEscape AbsolutePath=" + u2.AbsolutePath);
  Console.WriteLine("dontEscape AbsoluteUri=" + u2.AbsoluteUri);
  Console.WriteLine("dontEscape OriginalString=" + u2.OriginalString);
}}
