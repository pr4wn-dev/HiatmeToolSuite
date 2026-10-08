# Hiatme Tool Suite 4.0.0.74

Builder and Excel now share one published board. Builder still loads and
saves off OneDrive. After a Builder save, Desktop is updated so Excel
opened from Explorer has the same file. When Excel actually saves that
Desktop workbook, Tool Suite publishes those bytes so the other desk
sees them. OneDrive touching the file without Excel is ignored.
