 /
function hr FASM_STATE&fs $asm

if(hr=2) goto g1
if(hr>=0 or hr<-8) out "ERROR: %i" hr; ret
str errs=
 FASM_INVALID_PARAMETER		 = -1
 FASM_OUT_OF_MEMORY		 = -2
 FASM_STACK_OVERFLOW		 = -3
 FASM_SOURCE_NOT_FOUND		 = -4
 FASM_UNEXPECTED_END_OF_SOURCE	 = -5
 FASM_CANNOT_GENERATE_CODE	 = -6
 FASM_FORMAT_LIMITATIONS_EXCEDDED = -7
 FASM_WRITE_FAILED		 = -8

_s.getl(errs -hr-1)
_s.gett(_s+5 0); _s.lcase; _s.findreplace("_" " ")
out "ERROR: %s" _s
ret

 g1
hr=fs.error_code
if(hr>-101 or hr<-141) out "ERROR: %i" hr; ret
errs=
 FASMERR_FILE_NOT_FOUND			    = -101
 FASMERR_ERROR_READING_FILE		    = -102
 FASMERR_INVALID_FILE_FORMAT		    = -103
 FASMERR_INVALID_MACRO_ARGUMENTS 	    = -104
 FASMERR_INCOMPLETE_MACRO		    = -105
 FASMERR_UNEXPECTED_CHARACTERS		    = -106
 FASMERR_INVALID_ARGUMENT		    = -107
 FASMERR_ILLEGAL_INSTRUCTION		    = -108
 FASMERR_INVALID_OPERAND 		    = -109
 FASMERR_INVALID_OPERAND_SIZE		    = -110
 FASMERR_OPERAND_SIZE_NOT_SPECIFIED	    = -111
 FASMERR_OPERAND_SIZES_DO_NOT_MATCH	    = -112
 FASMERR_INVALID_ADDRESS_SIZE		    = -113
 FASMERR_ADDRESS_SIZES_DO_NOT_AGREE	    = -114
 FASMERR_DISALLOWED_COMBINATION_OF_REGISTERS = -115
 FASMERR_LONG_IMMEDIATE_NOT_ENCODABLE	    = -116
 FASMERR_RELATIVE_JUMP_OUT_OF_RANGE	    = -117
 FASMERR_INVALID_EXPRESSION		    = -118
 FASMERR_INVALID_ADDRESS 		    = -119
 FASMERR_INVALID_VALUE			    = -120
 FASMERR_VALUE_OUT_OF_RANGE		    = -121
 FASMERR_UNDEFINED_SYMBOL		    = -122
 FASMERR_INVALID_USE_OF_SYMBOL		    = -123
 FASMERR_NAME_TOO_LONG			    = -124
 FASMERR_INVALID_NAME			    = -125
 FASMERR_RESERVED_WORD_USED_AS_SYMBOL	    = -126
 FASMERR_SYMBOL_ALREADY_DEFINED		    = -127
 FASMERR_MISSING_END_QUOTE		    = -128
 FASMERR_MISSING_END_DIRECTIVE		    = -129
 FASMERR_UNEXPECTED_INSTRUCTION		    = -130
 FASMERR_EXTRA_CHARACTERS_ON_LINE	    = -131
 FASMERR_SECTION_NOT_ALIGNED_ENOUGH	    = -132
 FASMERR_SETTING_ALREADY_SPECIFIED	    = -133
 FASMERR_DATA_ALREADY_DEFINED		    = -134
 FASMERR_TOO_MANY_REPEATS		    = -135
 FASMERR_SYMBOL_OUT_OF_SCOPE		    = -136
 
 
 
 FASMERR_USER_ERROR			    = -140
 FASMERR_ASSERTION_FAILED		    = -141

_s.getl(errs -hr-101)
_s.gett(_s+8 0); _s.lcase; _s.findreplace("_" " ")
out "ERROR: %s" _s

LINE_HEADER& line=fs.error_line
_s.getl(asm+line.file_offset 0)
out "line %i: %s" line.line_number _s
