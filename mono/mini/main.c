#include <config.h>
#include "mini.h"
#ifndef HOST_WIN32
#include "buildver.h"
#endif

/* Set the process title via argv clobbering
	Note: this may need to be followed by a more platform specific method for completeness
		e.g. linux prctl
 */
static int set_process_title (int argc, void **argv, void *title, size_t title_len)
{
	void *last = argv[argc-1];
	size_t len = last - argv[0] + strlen(last);
	memset(*argv, 0, len);
	// BSD will count the number of arguments (argc) and expect to find that many
	// zero bytes before expecting the environment section to begin...without this, the
	// environment is shown in a process listing.
	len -= argc;
	if (len > title_len) len = title_len;
	memcpy(*argv, title, len);
	return len;
}

/* Determine if the --set-process-title=TITLE argument has been passed and if so set the process title to it */
static void handle_set_process_title (int argc, char *argv[])
{
	int i;
	for (i = 0; i < argc; i++){
		if (strncmp (argv[i], "--set-process-title=", 20) == 0) {
			size_t length = strlen (argv[i]) - 20;
			char *title = (char*)malloc(sizeof(char*)*length);
			strcpy (title, argv[i]+20);
			int val = set_process_title (argc, (void **)argv, title, length);
			return;
		}
	}
}

/*
 * If the MONO_ENV_OPTIONS environment variable is set, it uses this as a
 * source of command line arguments that are passed to Mono before the
 * command line arguments specified in the command line.
 */
static int
mono_main_with_options (int argc, char *argv [])
{
	int old_argc = argc;
	char **old_argv = argv;

	const char *env_options = getenv ("MONO_ENV_OPTIONS");
	if (env_options != NULL){
		GPtrArray *array = g_ptr_array_new ();
		GString *buffer = g_string_new ("");
		const char *p;
		int i;
		gboolean in_quotes = FALSE;
		char quote_char = '\0';

		for (p = env_options; *p; p++){
			switch (*p){
			case ' ': case '\t':
				if (!in_quotes) {
					if (buffer->len != 0){
						g_ptr_array_add (array, g_strdup (buffer->str));
						g_string_truncate (buffer, 0);
					}
				} else {
					g_string_append_c (buffer, *p);
				}
				break;
			case '\\':
				if (p [1]){
					g_string_append_c (buffer, p [1]);
					p++;
				}
				break;
			case '\'':
			case '"':
				if (in_quotes) {
					if (quote_char == *p)
						in_quotes = FALSE;
					else
						g_string_append_c (buffer, *p);
				} else {
					in_quotes = TRUE;
					quote_char = *p;
				}
				break;
			default:
				g_string_append_c (buffer, *p);
				break;
			}
		}
		if (in_quotes) {
			fprintf (stderr, "Unmatched quotes in value of MONO_ENV_OPTIONS: [%s]\n", env_options);
			exit (1);
		}
			
		if (buffer->len != 0)
			g_ptr_array_add (array, g_strdup (buffer->str));
		g_string_free (buffer, TRUE);

		if (array->len > 0){
			int new_argc = array->len + argc;
			char **new_argv = g_new (char *, new_argc + 1);
			int j;

			new_argv [0] = argv [0];
			
			/* First the environment variable settings, to allow the command line options to override */
			for (i = 0; i < array->len; i++)
				new_argv [i+1] = g_ptr_array_index (array, i);
			i++;
			for (j = 1; j < argc; j++)
				new_argv [i++] = argv [j];
			new_argv [i] = NULL;

			argc = new_argc;
			argv = new_argv;
		}
		g_ptr_array_free (array, TRUE);
	}

#ifndef HOST_WIN32
	/* pass on a copy of the real argv before possibly clobbering it */
	if (old_argv == argv){
		int i;
		argv = (char**)malloc(sizeof(char**)*(old_argc+1));
		for (i = 0; i < old_argc; i++){
			argv[i] = (char*)malloc(sizeof(char*)*strlen(old_argv[i]));
			strcpy(argv[i], old_argv[i]);
		}
		argv[i] = NULL;
	}

	handle_set_process_title(old_argc, old_argv);
#endif

	return mono_main (argc, argv);
}

#ifdef HOST_WIN32

int
main ()
{
	int argc;
	gunichar2** argvw;
	gchar** argv;
	int i;

	argvw = CommandLineToArgvW (GetCommandLine (), &argc);
	argv = g_new0 (gchar*, argc + 1);
	for (i = 0; i < argc; i++)
		argv [i] = g_utf16_to_utf8 (argvw [i], -1, NULL, NULL, NULL);
	argv [argc] = NULL;

	LocalFree (argvw);

	return mono_main_with_options  (argc, argv);
}

#else

int
main (int argc, char* argv[])
{
	mono_build_date = build_date;
	
	return mono_main_with_options (argc, argv);
}

#endif
