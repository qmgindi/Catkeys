str s=
 A1,B1,C1
 A2,B2,C2
 A3,B3,C3
 A4,B4,C4
 A5,B5,C5
 A6,B6,C6
 A7,B7,C7
 A8,B8,C8
 A9,"a,b
 c",C9
 A,"B ""Q"" Z",C

PF
rep 1000
	ICsv x._create
	x.FromString(s)
	int i n=x.RowCount
	for i 0 n
		str t=x.Cell(i 1)
		
PN
PO